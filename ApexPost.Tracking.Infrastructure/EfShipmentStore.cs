using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Tracking.Infrastructure;

public sealed class EfShipmentStore(TrackingDbContext db) : IShipmentStore
{
    // SQL Server errors for a duplicate key in a unique index or a primary key.
    private static readonly int[] DuplicateKeyErrors = [2601, 2627];

    public async Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default)
    {
        var shipments = db.Shipments.AsNoTracking();
        if (query.Status is { } status)
        {
            shipments = shipments.Where(shipment => shipment.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            // The column uses the database's case-insensitive collation, so "sydney" finds "Sydney".
            var city = query.City.Trim();
            shipments = shipments.Where(shipment => shipment.City == city);
        }

        var total = await shipments.CountAsync(ct);
        var page = await Project(shipments
                .OrderByDescending(shipment => shipment.CreatedAtUtc)
                .ThenBy(shipment => shipment.TrackingNumber)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize))
            .ToListAsync(ct);
        return new PagedResult<ShipmentView>(page, query.Page, query.PageSize, total);
    }

    public Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default) =>
        Project(db.Shipments.AsNoTracking().Where(shipment => shipment.TrackingNumber == trackingNumber)).FirstOrDefaultAsync(ct);

    public Task<Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default) =>
        db.Shipments.FirstOrDefaultAsync(shipment => shipment.TrackingNumber == trackingNumber, ct);

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default) =>
        db.Shipments.AnyAsync(shipment => shipment.TrackingNumber == trackingNumber, ct);

    public void Add(Shipment shipment) => db.Shipments.Add(shipment);

    public void Remove(Shipment shipment) => db.Shipments.Remove(shipment);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException("The shipment was changed or deleted by another request.", exception);
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            throw new DuplicateValueException("A shipment with this tracking number already exists.", exception);
        }
    }

    public async Task<IReadOnlyList<TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default) =>
        await db.Scans.AsNoTracking()
            .Where(scan => scan.ShipmentId == shipmentId)
            .OrderBy(scan => scan.ScannedAtUtc)
            .ThenBy(scan => scan.ReceivedAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default)
    {
        if (scanIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await db.Scans.AsNoTracking()
            .Where(scan => scanIds.Contains(scan.ScanId))
            .ToDictionaryAsync(scan => scan.ScanId, scan => scan.ShipmentId, ct);
    }

    public async Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<TrackingScan> scans, CancellationToken ct = default)
    {
        db.Scans.AddRange(scans);
        try
        {
            await db.SaveChangesAsync(ct);
            return scans.Select(scan => scan.ScanId).ToHashSet();
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Another upload of the same scans won the race. Nothing from this batch was written, because SaveChanges
            // runs in one transaction, so insert them one at a time and skip the ones that are already there.
            Detach(scans);
        }

        var stored = new HashSet<Guid>();
        foreach (var scan in scans)
        {
            db.Scans.Add(scan);
            try
            {
                await db.SaveChangesAsync(ct);
                stored.Add(scan.ScanId);
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                Detach([scan]);
            }
        }

        return stored;
    }

    private void Detach(IEnumerable<TrackingScan> scans)
    {
        foreach (var scan in scans)
        {
            db.Entry(scan).State = EntityState.Detached;
        }
    }

    private IQueryable<ShipmentView> Project(IQueryable<Shipment> shipments) =>
        shipments.Select(shipment => new ShipmentView(
            shipment.TrackingNumber,
            shipment.OriginBranchCode,
            new Recipient(shipment.RecipientName, shipment.RecipientPhone, shipment.RecipientAddress, shipment.City),
            shipment.WeightKg,
            shipment.Status,
            shipment.CreatedAtUtc,
            shipment.UpdatedAtUtc,
            db.Scans
                .Where(scan => scan.ShipmentId == shipment.Id)
                .OrderByDescending(scan => scan.ScannedAtUtc)
                .ThenByDescending(scan => scan.ReceivedAtUtc)
                .Select(scan => new ScanSummary(scan.ScanType, scan.Location, scan.ScannedAtUtc))
                .FirstOrDefault()));

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && DuplicateKeyErrors.Contains(sql.Number);
}
