namespace ApexPost.Courier.Offline.Storage;

/// <summary>Raised after documents in the scans collection change. It carries the live pending count.</summary>
public sealed class ScansChangedEventArgs(IReadOnlyList<string> documentIds, int pendingCount) : EventArgs
{
    /// <summary>The ids of the documents that were saved or deleted.</summary>
    public IReadOnlyList<string> DocumentIds { get; } = documentIds;

    /// <summary>How many scans have syncStatus = Pending right after the change.</summary>
    public int PendingCount { get; } = pendingCount;
}
