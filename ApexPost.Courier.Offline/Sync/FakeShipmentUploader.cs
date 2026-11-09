using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// Stands in for the server in Part A. It accepts every scan except one whose notes contain <c>[fail]</c>, which lets a
/// run show the Failed outcome. It records the id of every scan it was asked to upload, in order.
/// </summary>
public sealed class FakeShipmentUploader : IShipmentUploader
{
    public const string FailureMarker = "[fail]";

    private readonly List<string> _attempts = [];

    /// <summary>The ids of the scans passed to <see cref="UploadAsync"/>, in the order they arrived.</summary>
    public IReadOnlyList<string> Attempts
    {
        get
        {
            lock (_attempts)
            {
                return [.. _attempts];
            }
        }
    }

    public Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_attempts)
        {
            _attempts.Add(scan.Id);
        }

        var rejected = scan.Notes?.Contains(FailureMarker, StringComparison.OrdinalIgnoreCase) == true;
        return Task.FromResult(rejected
            ? UploadResult.Failure($"The fake server rejected the scan because its notes contain {FailureMarker}.")
            : UploadResult.Success);
    }
}
