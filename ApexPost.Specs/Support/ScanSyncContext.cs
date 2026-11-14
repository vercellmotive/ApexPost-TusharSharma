using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Testing.Offline;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Application;
using Microsoft.Extensions.Time.Testing;
using TrackingScanType = ApexPost.Tracking.Domain.ScanType;

namespace ApexPost.Specs.Support;

/// <summary>
/// The device and the server of one ScanSync scenario, shared between steps by context injection. The device is a
/// real Couchbase Lite database in a temporary folder, opened and deleted by <see cref="ScenarioHooks"/>. The server
/// is the real <see cref="ShipmentService"/> over an in-memory store, with a fixed clock.
/// </summary>
public sealed class ScanSyncContext
{
    public TestCourierDatabase Device { get; set; } = null!;

    public InMemoryShipmentStore Server { get; } = new();

    public FakeTimeProvider Clock { get; } = TestTime.NewClock();

    public string TrackingNumber { get; set; } = string.Empty;

    public string CourierId { get; set; } = string.Empty;

    public DateTimeOffset PickedUpAt { get; set; }

    public ShipmentScan? LastSaved { get; set; }

    public OutboxResult? LastSync { get; set; }

    /// <summary>The Part A outbox, with the uploader that hands each scan to the server in process.</summary>
    public OutboxService CreateOutbox() =>
        new(Device.Repository, new InProcessShipmentUploader(new ShipmentService(Server, new FakeBranches(), Clock), CourierId));
}

/// <summary>
/// An <see cref="IShipmentUploader"/> that calls the server's service directly instead of over HTTP, mapping the
/// device's scan to the server's model the same way the API's request mapping does.
/// </summary>
public sealed class InProcessShipmentUploader(IShipmentService server, string signedInCourierId) : IShipmentUploader
{
    public async Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default)
    {
        var incoming = new IncomingScan(
            scan.Id,
            Enum.Parse<TrackingScanType>(scan.ScanType.ToString()),
            scan.CourierId,
            scan.Location,
            scan.ScannedAtUtc,
            scan.Notes,
            scan.RecipientSignatureName);
        var result = await server.UploadScansAsync(scan.TrackingNumber, [incoming], signedInCourierId, cancellationToken);
        if (!result.Succeeded)
        {
            return UploadResult.Failure(result.Error.Detail);
        }

        // Accepted and skipped-as-duplicate both mean the server has the scan.
        var rejection = result.Value.Rejections.FirstOrDefault();
        return rejection is null ? UploadResult.Success : UploadResult.Failure(rejection.Reason);
    }
}
