namespace ApexPost.Courier.Core.Sync;

/// <summary>A scan the server refused, and why.</summary>
public sealed record SyncFailure(string ScanId, string TrackingNumber, string Reason);

/// <summary>What one sync run did.</summary>
/// <param name="Synced">Scans the server now has, including ones it already had.</param>
/// <param name="Failed">Scans the server refused. They are marked Failed and not sent again.</param>
/// <param name="LeftPending">Scans not sent because the server could not be reached. They stay Pending.</param>
/// <param name="StopReason">Why the run stopped early, or null when it finished.</param>
public sealed record SyncReport(
    DateTimeOffset FinishedAtUtc,
    int Synced,
    int Failed,
    int LeftPending,
    IReadOnlyList<SyncFailure> Failures,
    string? StopReason)
{
    public static SyncReport Skipped(DateTimeOffset now, string reason) => new(now, 0, 0, 0, [], reason);
}
