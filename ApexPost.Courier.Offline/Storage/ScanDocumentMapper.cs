using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>The names of the seven fields in a ShipmentScan document.</summary>
internal static class ScanFields
{
    public const string TrackingNumber = "trackingNumber";
    public const string ScanType = "scanType";
    public const string CourierId = "courierId";
    public const string Location = "location";
    public const string ScannedAtUtc = "scannedAtUtc";
    public const string Notes = "notes";
    public const string SyncStatus = "syncStatus";

    public static readonly string[] All = [TrackingNumber, ScanType, CourierId, Location, ScannedAtUtc, Notes, SyncStatus];
}

/// <summary>
/// The only place where a domain scan and a Couchbase document meet. Everything outside the Storage namespace
/// sees <see cref="ShipmentScan"/> and nothing else.
/// </summary>
internal static class ScanDocumentMapper
{
    public static MutableDocument ToNewDocument(ShipmentScan scan)
    {
        var document = new MutableDocument(scan.Id);
        Apply(document, scan);
        return document;
    }

    /// <summary>Writes every field. Enums go in as their names, and the time goes in as UTC so text order is time order.</summary>
    public static void Apply(MutableDocument document, ShipmentScan scan)
    {
        document.SetString(ScanFields.TrackingNumber, scan.TrackingNumber);
        document.SetString(ScanFields.ScanType, scan.ScanType.ToString());
        document.SetString(ScanFields.CourierId, scan.CourierId);
        document.SetString(ScanFields.Location, scan.Location);
        document.SetDate(ScanFields.ScannedAtUtc, scan.ScannedAtUtc.ToUniversalTime());
        document.SetString(ScanFields.Notes, scan.Notes);
        document.SetString(ScanFields.SyncStatus, scan.SyncStatus.ToString());
    }

    /// <summary>Maps a document that was read by key.</summary>
    public static ShipmentScan FromDocument(Document document) => FromData(document.Id, document);

    /// <summary>Maps one row of a query that selects <c>META().id AS id</c> and the seven fields.</summary>
    public static ShipmentScan FromResult(Result row) =>
        FromData(row.GetString("id") ?? throw new InvalidDataException("A query row has no document id."), row);

    private static ShipmentScan FromData(string id, IDictionaryObject data) => new()
    {
        Id = id,
        TrackingNumber = Text(data, id, ScanFields.TrackingNumber),
        ScanType = Name<ScanType>(Text(data, id, ScanFields.ScanType), id, ScanFields.ScanType),
        CourierId = Text(data, id, ScanFields.CourierId),
        Location = Text(data, id, ScanFields.Location),
        ScannedAtUtc = Time(data, id),
        Notes = data.GetString(ScanFields.Notes),
        SyncStatus = Name<SyncStatus>(Text(data, id, ScanFields.SyncStatus), id, ScanFields.SyncStatus)
    };

    private static string Text(IDictionaryObject data, string id, string field) =>
        data.GetString(field) ?? throw new InvalidDataException($"Document {id} has no '{field}' text.");

    private static DateTimeOffset Time(IDictionaryObject data, string id)
    {
        if (!data.Contains(ScanFields.ScannedAtUtc))
        {
            throw new InvalidDataException($"Document {id} has no '{ScanFields.ScannedAtUtc}'.");
        }

        var value = data.GetDate(ScanFields.ScannedAtUtc);
        return value == DateTimeOffset.MinValue
            ? throw new InvalidDataException($"Document {id} has an unreadable '{ScanFields.ScannedAtUtc}'.")
            : value.ToUniversalTime();
    }

    // Accepts only the exact member name, so "1" or "pickup" in a document is reported rather than guessed at.
    private static TEnum Name<TEnum>(string text, string id, string field)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(text, ignoreCase: false, out var value) && Enum.IsDefined(value) && value.ToString() == text
            ? value
            : throw new InvalidDataException($"Document {id} has '{text}' in '{field}', which is not a {typeof(TEnum).Name}.");
}
