using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// Keeps <see cref="ShipmentScan"/> documents in the default collection of a <see cref="CourierDatabase"/>.
/// Create, get, update and delete are key-value operations on the document id. The lists and counts are SQL++ queries.
/// </summary>
public sealed class CouchbaseScanRepository(CourierDatabase database) : IScanRepository
{
    private const int MaxConflictRetries = 5;

    public ShipmentScan Create(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        scan.Validate();
        if (scan.SyncStatus != SyncStatus.Pending)
        {
            throw new ArgumentException("A new scan is saved locally as Pending. Only the outbox changes the status.", nameof(scan));
        }

        // FailOnConflict makes Save refuse to replace a document that already has this id.
        if (!database.DefaultCollection.Save(ScanDocumentMapper.ToNewDocument(scan), ConcurrencyControl.FailOnConflict))
        {
            throw new InvalidOperationException($"A scan with id {scan.Id} already exists.");
        }

        return scan;
    }

    public ShipmentScan? Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        using var document = database.DefaultCollection.GetDocument(id);
        return document is null ? null : ScanDocumentMapper.FromDocument(document);
    }

    public bool Update(ShipmentScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        scan.Validate();
        var collection = database.DefaultCollection;
        using var current = collection.GetDocument(scan.Id);
        if (current is null)
        {
            return false;
        }

        // The caller supplies the whole scan, so the caller's version wins.
        using var changed = current.ToMutable();
        ScanDocumentMapper.Apply(changed, scan);
        return collection.Save(changed, ConcurrencyControl.LastWriteWins);
    }

    public bool UpdateSyncStatus(string id, SyncStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        var collection = database.DefaultCollection;
        for (var attempt = 0; attempt < MaxConflictRetries; attempt++)
        {
            using var current = collection.GetDocument(id);
            if (current is null)
            {
                return false;
            }

            using var changed = current.ToMutable();
            changed.SetString(ScanFields.SyncStatus, status.ToString());

            // FailOnConflict: if someone saved the document since we read it, read it again instead of overwriting their change.
            if (collection.Save(changed, ConcurrencyControl.FailOnConflict))
            {
                return true;
            }
        }

        throw new InvalidOperationException($"Scan {id} kept changing while its sync status was being updated.");
    }

    public bool Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var collection = database.DefaultCollection;
        using var current = collection.GetDocument(id);
        return current is not null && collection.Delete(current, ConcurrencyControl.LastWriteWins);
    }

    public IReadOnlyList<ShipmentScan> GetAll() => RunQuery(ScanQueries.All, bind: null);

    public IReadOnlyList<ShipmentScan> FindByTrackingNumber(string trackingNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackingNumber);
        return RunQuery(
            ScanQueries.ByTrackingNumber,
            parameters => parameters.SetString(ScanQueries.TrackingNumberParameter, trackingNumber));
    }

    public IReadOnlyList<ShipmentScan> FindPending() =>
        RunQuery(
            ScanQueries.ByStatus,
            parameters => parameters.SetString(ScanQueries.StatusParameter, SyncStatus.Pending.ToString()));

    public IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        var counts = Enum.GetValues<ScanType>().ToDictionary(type => type, _ => 0);

        using var query = database.CreateQuery(ScanQueries.CountByScanType);
        query.Parameters = new Parameters().SetString(ScanQueries.CourierParameter, courierId);
        using var results = query.Execute();
        foreach (var row in results.AllResults())
        {
            var name = row.GetString(ScanFields.ScanType);
            if (Enum.TryParse<ScanType>(name, ignoreCase: false, out var type) && Enum.IsDefined(type) && type.ToString() == name)
            {
                counts[type] = row.GetInt("total");
            }
        }

        return counts;
    }

    public int CountPending() => database.CountPending();

    /// <summary>
    /// The SQL++ text of each statement with the access path Couchbase Lite chose for it (its EXPLAIN output),
    /// so it is easy to see which index a query uses.
    /// </summary>
    public IReadOnlyList<QueryPlan> ExplainQueries() =>
    [
        Explain("All scans for a tracking number", ScanQueries.ByTrackingNumber, ScanQueries.TrackingNumberParameter, "APX-20260001"),
        Explain("All scans where syncStatus = Pending", ScanQueries.ByStatus, ScanQueries.StatusParameter, nameof(SyncStatus.Pending)),
        Explain("Number of scans per scanType for one courier", ScanQueries.CountByScanType, ScanQueries.CourierParameter, "C-1001"),
        Explain("Number of pending scans", ScanQueries.CountByStatus, ScanQueries.StatusParameter, nameof(SyncStatus.Pending))
    ];

    private QueryPlan Explain(string name, string sql, string parameter, string value)
    {
        using var query = database.CreateQuery(sql);
        query.Parameters = new Parameters().SetString(parameter, value);

        // EXPLAIN prints the access path on lines shaped like "3|0|61| SEARCH _ USING INDEX idx_syncStatus (<expr>=?)".
        var steps = query.Explain()
            .Split('\n')
            .Select(line => line.Split('|'))
            .Where(parts => parts.Length == 4 && parts[..3].All(part => int.TryParse(part, out _)))
            .Select(parts => parts[3].Trim());
        return new QueryPlan(name, sql, string.Join(" ; ", steps));
    }

    // Rows are turned into scans before the result set is disposed.
    private List<ShipmentScan> RunQuery(string sql, Action<Parameters>? bind)
    {
        using var query = database.CreateQuery(sql);
        if (bind is not null)
        {
            var parameters = new Parameters();
            bind(parameters);
            query.Parameters = parameters;
        }

        using var results = query.Execute();
        return [.. results.AllResults().Select(ScanDocumentMapper.FromResult)];
    }
}
