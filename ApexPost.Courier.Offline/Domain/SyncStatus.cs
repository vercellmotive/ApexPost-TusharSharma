namespace ApexPost.Courier.Offline.Domain;

/// <summary>Where a scan is on its way to the server. A new scan is always Pending; only the outbox moves it on.</summary>
public enum SyncStatus
{
    Pending,
    Synced,
    Failed
}
