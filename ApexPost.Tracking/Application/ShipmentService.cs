using ApexPost.Application.Interfaces;
using ApexPost.Domain.ValueObjects;
using ApexPost.Tracking.Domain;

namespace ApexPost.Tracking.Application;

public sealed class ShipmentService(IShipmentStore store, IBranchRepository branches, TimeProvider clock) : IShipmentService
{
    /// <summary>How far a courier device's clock may drift from the server's before a scan time is rejected.</summary>
    public static readonly TimeSpan ScanClockTolerance = TimeSpan.FromMinutes(5);

    public Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, ShipmentQuery.MaximumPageSize);
        return store.ListAsync(query, ct);
    }

    public async Task<ServiceResult<ShipmentView>> GetAsync(string trackingNumber, CancellationToken ct = default)
    {
        var view = await store.GetViewAsync(trackingNumber, ct);
        return view is null ? ServiceError.ShipmentNotFound(trackingNumber) : ServiceResult<ShipmentView>.Success(view);
    }

    public async Task<ServiceResult<ShipmentView>> CreateAsync(NewShipment request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The origin branch comes from the Week 3 data: it must exist and be open for business.
        var branch = await branches.GetByCodeAsync(new BranchCode(request.OriginBranchCode), ct);
        if (branch is null || !branch.IsActive)
        {
            var reason = branch is null ? "does not exist" : "is closed";
            return new ServiceError(
                ServiceErrorKind.Validation,
                "Unknown origin branch",
                $"Branch {request.OriginBranchCode} {reason}.",
                nameof(NewShipment.OriginBranchCode));
        }

        if (await store.ExistsAsync(request.TrackingNumber, ct))
        {
            return DuplicateTrackingNumber(request.TrackingNumber);
        }

        var shipment = Shipment.Create(request.TrackingNumber, request.OriginBranchCode, request.Recipient, request.WeightKg, clock.GetUtcNow());
        store.Add(shipment);
        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (DuplicateValueException)
        {
            // Another request created the same tracking number between the check and the insert.
            return DuplicateTrackingNumber(request.TrackingNumber);
        }

        return ServiceResult<ShipmentView>.Success(ShipmentView.From(shipment, latestScan: null));
    }

    public async Task<ServiceResult<ShipmentView>> ReplaceDetailsAsync(string trackingNumber, ShipmentDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var shipment = await store.FindAsync(trackingNumber, ct);
        if (shipment is null)
        {
            return ServiceError.ShipmentNotFound(trackingNumber);
        }

        if (shipment.IsClosed)
        {
            return new ServiceError(
                ServiceErrorKind.Conflict,
                "Shipment is closed",
                $"Shipment {trackingNumber} is {shipment.Status}, so its details can no longer change.");
        }

        shipment.ReplaceDetails(details.Recipient, details.WeightKg, clock.GetUtcNow());
        return await SaveAndReadAsync(shipment, ct);
    }

    public async Task<ServiceResult<ShipmentView>> ChangeStatusAsync(string trackingNumber, ShipmentStatus status, CancellationToken ct = default)
    {
        var shipment = await store.FindAsync(trackingNumber, ct);
        if (shipment is null)
        {
            return ServiceError.ShipmentNotFound(trackingNumber);
        }

        if (!shipment.CanMoveTo(status))
        {
            var allowed = Shipment.AllowedNext(shipment.Status);
            var next = allowed.Count == 0 ? "nothing, because it is final" : string.Join(" or ", allowed);
            return new ServiceError(
                ServiceErrorKind.Conflict,
                "Invalid status transition",
                $"Shipment {trackingNumber} is {shipment.Status} and cannot move to {status}. It can move to {next}.");
        }

        shipment.MoveTo(status, clock.GetUtcNow());
        return await SaveAndReadAsync(shipment, ct);
    }

    public async Task<ServiceResult<Done>> DeleteAsync(string trackingNumber, CancellationToken ct = default)
    {
        var shipment = await store.FindAsync(trackingNumber, ct);
        if (shipment is null)
        {
            return ServiceError.ShipmentNotFound(trackingNumber);
        }

        if (!shipment.CanBeDeleted)
        {
            return new ServiceError(
                ServiceErrorKind.Conflict,
                "Shipment cannot be deleted",
                $"Shipment {trackingNumber} is {shipment.Status}. Only a shipment in {ShipmentStatus.Created} status can be deleted.");
        }

        store.Remove(shipment);
        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return ConcurrentChange(trackingNumber);
        }

        return ServiceResult<Done>.Success(default);
    }

    public async Task<ServiceResult<IReadOnlyList<TrackingScan>>> GetScansAsync(string trackingNumber, CancellationToken ct = default)
    {
        var shipment = await store.FindAsync(trackingNumber, ct);
        return shipment is null
            ? ServiceError.ShipmentNotFound(trackingNumber)
            : ServiceResult<IReadOnlyList<TrackingScan>>.Success(await store.GetScansAsync(shipment.Id, ct));
    }

    /// <summary>
    /// Stores the scans that are new. The scan id is the idempotency key: a scan already stored for this shipment
    /// counts as a duplicate and is skipped, so a courier device can upload the same batch again after a lost reply.
    /// </summary>
    public async Task<ServiceResult<ScanUploadOutcome>> UploadScansAsync(
        string trackingNumber,
        IReadOnlyList<IncomingScan> scans,
        string uploadingCourierId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scans);

        // A courier uploads only the scans they made. One scan under another courier id refuses the whole batch,
        // because it means the device or the token is wrong, not that one scan is bad.
        var foreign = scans.FirstOrDefault(scan => !string.Equals(scan.CourierId, uploadingCourierId, StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(uploadingCourierId) || foreign is not null)
        {
            return new ServiceError(
                ServiceErrorKind.Forbidden,
                "Not your scans",
                foreign is null
                    ? "The access token has no courierId claim, so it cannot upload scans."
                    : $"Scan {foreign.ScanId} was recorded by courier {foreign.CourierId}. You can upload only scans recorded under your own courier id.");
        }

        var shipment = await store.FindAsync(trackingNumber, ct);
        if (shipment is null)
        {
            return ServiceError.ShipmentNotFound(trackingNumber);
        }

        var now = clock.GetUtcNow();
        var rejections = new List<ScanRejection>();
        var duplicates = 0;
        var candidates = new List<(Guid Id, IncomingScan Scan)>();
        var seen = new HashSet<Guid>();
        foreach (var scan in scans)
        {
            if (!ScanId.TryParse(scan.ScanId, out var id))
            {
                rejections.Add(new ScanRejection(scan.ScanId, $"The scan id must look like {ScanId.Prefix}{{Guid}}."));
            }
            else if (!seen.Add(id))
            {
                // The same scan twice in one batch is a duplicate, not an error.
                duplicates++;
            }
            else if (RejectionReason(scan, shipment, now) is { } reason)
            {
                rejections.Add(new ScanRejection(scan.ScanId, reason));
            }
            else
            {
                candidates.Add((id, scan));
            }
        }

        var owners = await store.FindScanShipmentsAsync(candidates.Select(candidate => candidate.Id).ToList(), ct);
        var fresh = new List<TrackingScan>();
        foreach (var (id, scan) in candidates)
        {
            if (!owners.TryGetValue(id, out var ownerId))
            {
                fresh.Add(TrackingScan.Record(
                    id, shipment.Id, scan.ScanType, scan.CourierId, scan.Location, scan.ScannedAtUtc, scan.Notes, scan.RecipientSignatureName, now));
            }
            else if (ownerId == shipment.Id)
            {
                duplicates++;
            }
            else
            {
                rejections.Add(new ScanRejection(scan.ScanId, "This scan id is already recorded for another shipment."));
            }
        }

        var stored = fresh.Count == 0 ? new HashSet<Guid>() : await store.AddScansAsync(fresh, ct);

        // A scan that another upload stored in the meantime was skipped by the store, so it is a duplicate too.
        duplicates += fresh.Count - stored.Count;
        return ServiceResult<ScanUploadOutcome>.Success(new ScanUploadOutcome(stored.Count, duplicates, rejections));
    }

    private static string? RejectionReason(IncomingScan scan, Shipment shipment, DateTimeOffset now)
    {
        if (scan.ScannedAtUtc > now + ScanClockTolerance)
        {
            return "The scan time is in the future.";
        }

        if (scan.ScannedAtUtc < shipment.CreatedAtUtc - ScanClockTolerance)
        {
            return "The scan time is before the shipment was created.";
        }

        if (scan.ScanType == ScanType.Delivered && string.IsNullOrWhiteSpace(scan.RecipientSignatureName))
        {
            return "A Delivered scan needs the name of the person who signed for it.";
        }

        return null;
    }

    private async Task<ServiceResult<ShipmentView>> SaveAndReadAsync(Shipment shipment, CancellationToken ct)
    {
        try
        {
            await store.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return ConcurrentChange(shipment.TrackingNumber);
        }

        var view = await store.GetViewAsync(shipment.TrackingNumber, ct);
        return view is null ? ServiceError.ShipmentNotFound(shipment.TrackingNumber) : ServiceResult<ShipmentView>.Success(view);
    }

    private static ServiceError DuplicateTrackingNumber(string trackingNumber) => new(
        ServiceErrorKind.Conflict,
        "Duplicate tracking number",
        $"A shipment with tracking number {trackingNumber} already exists.");

    private static ServiceError ConcurrentChange(string trackingNumber) => new(
        ServiceErrorKind.Conflict,
        "Shipment changed",
        $"Shipment {trackingNumber} was changed by another request. Read it again and retry.");
}
