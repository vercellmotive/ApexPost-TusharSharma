namespace ApexPost.Courier.Offline.Sync;

/// <summary>One scan the uploader could not accept, and why.</summary>
public sealed record OutboxFailure(string ScanId, string Error);

/// <summary>What one run of the outbox did.</summary>
/// <param name="SyncedIds">Scans that were uploaded and are now Synced.</param>
/// <param name="Failures">Scans that were not accepted and are now Failed.</param>
/// <param name="SkippedIds">Scans that were deleted while their upload was running, so there was nothing left to mark.</param>
/// <param name="DeferredIds">Scans left Pending because the server could not be reached. The next run tries them again.</param>
public sealed record OutboxResult(
    IReadOnlyList<string> SyncedIds,
    IReadOnlyList<OutboxFailure> Failures,
    IReadOnlyList<string> SkippedIds,
    IReadOnlyList<string>? DeferredIds = null)
{
    public int Deferred => DeferredIds?.Count ?? 0;

    public int Synced => SyncedIds.Count;

    public int Failed => Failures.Count;

    public int Skipped => SkippedIds.Count;
}
