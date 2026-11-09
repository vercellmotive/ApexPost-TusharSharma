namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Where the courier's scans are kept on the device. The interface speaks only in domain types, so the rest of
/// the app never learns which database sits behind it.
/// </summary>
public interface IScanRepository
{
    /// <summary>Saves a new scan locally. The scan must be Pending and its id must not exist yet.</summary>
    ShipmentScan Create(ShipmentScan scan);

    /// <summary>Key-value lookup by document id. Returns null when there is no such scan.</summary>
    ShipmentScan? Get(string id);

    /// <summary>Replaces the stored scan that has the same id. Returns false when it no longer exists.</summary>
    bool Update(ShipmentScan scan);

    /// <summary>Changes only the sync status of a stored scan, so a concurrent edit to other fields is kept.</summary>
    bool UpdateSyncStatus(string id, SyncStatus status);

    /// <summary>Deletes a scan by id. Returns false when it does not exist.</summary>
    bool Delete(string id);

    /// <summary>Every scan, oldest first.</summary>
    IReadOnlyList<ShipmentScan> GetAll();

    /// <summary>All scans for one tracking number, ordered by scannedAtUtc.</summary>
    IReadOnlyList<ShipmentScan> FindByTrackingNumber(string trackingNumber);

    /// <summary>All scans whose syncStatus is Pending, oldest first.</summary>
    IReadOnlyList<ShipmentScan> FindPending();

    /// <summary>The number of scans of each scan type for one courier. Every type is present, with 0 when there are none.</summary>
    IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId);

    /// <summary>How many scans are still waiting to be uploaded.</summary>
    int CountPending();
}
