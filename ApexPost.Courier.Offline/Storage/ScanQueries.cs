namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// Every SQL++ statement the repository runs. Each one is a single complete literal, and every value reaches it as a
/// query parameter ($name), so nothing is ever joined into the text. <c>FROM _</c> is the default collection.
/// </summary>
internal static class ScanQueries
{
    public const string TrackingNumberParameter = "trackingNumber";
    public const string StatusParameter = "syncStatus";
    public const string CourierParameter = "courierId";

    /// <summary>All scans, oldest first.</summary>
    public const string All = """SELECT META().id AS id, trackingNumber, scanType, courierId, location, scannedAtUtc, notes, syncStatus, recipientSignatureName FROM _ ORDER BY scannedAtUtc, META().id""";

    /// <summary>All scans for a tracking number, ordered by scannedAtUtc.</summary>
    public const string ByTrackingNumber = """SELECT META().id AS id, trackingNumber, scanType, courierId, location, scannedAtUtc, notes, syncStatus, recipientSignatureName FROM _ WHERE trackingNumber = $trackingNumber ORDER BY scannedAtUtc, META().id""";

    /// <summary>All scans with a given syncStatus (the repository passes Pending), oldest first.</summary>
    public const string ByStatus = """SELECT META().id AS id, trackingNumber, scanType, courierId, location, scannedAtUtc, notes, syncStatus, recipientSignatureName FROM _ WHERE syncStatus = $syncStatus ORDER BY scannedAtUtc, META().id""";

    /// <summary>The number of scans of each scan type for one courier.</summary>
    public const string CountByScanType = """SELECT scanType, COUNT(*) AS total FROM _ WHERE courierId = $courierId GROUP BY scanType""";

    /// <summary>The number of scans with a given syncStatus.</summary>
    public const string CountByStatus = """SELECT COUNT(*) AS total FROM _ WHERE syncStatus = $syncStatus""";
}
