using ApexPost.Courier.Offline.Domain;
using Couchbase.Lite;
using Couchbase.Lite.Query;

namespace ApexPost.Courier.Offline.Storage;

/// <summary>
/// The courier's local Couchbase Lite database. It opens <c>apexpost_courier</c>, works in the default
/// collection, creates the value indexes, and listens for changes. Dispose it to remove the listener and close the file.
/// One process should own the database folder: see OFFLINE-DESIGN.md.
/// </summary>
public sealed class CourierDatabase : IDisposable
{
    public const string DefaultName = "apexpost_courier";
    public const string TrackingNumberIndexName = "idx_trackingNumber";
    public const string SyncStatusIndexName = "idx_syncStatus";

    private readonly Database _database;
    private readonly Collection _collection;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private ListenerToken? _changeToken;
    private int _listening;
    private int _pendingCount;
    private bool _disposed;

    public CourierDatabase(CourierDatabaseOptions? options = null)
    {
        options ??= new CourierDatabaseOptions();
        _log = options.Log;

        DatabaseConfiguration configuration;
        if (options.Directory is null)
        {
            configuration = new DatabaseConfiguration();
        }
        else
        {
            System.IO.Directory.CreateDirectory(options.Directory);
            configuration = new DatabaseConfiguration { Directory = options.Directory };
        }

        _database = new Database(options.Name, configuration);
        try
        {
            _collection = _database.GetDefaultCollection();
            EnsureIndexes();
            _pendingCount = CountPending();

            // The collection change listener: it logs the ids of changed documents and refreshes the pending count.
            // The token is kept so Dispose can remove the listener.
            _changeToken = _collection.AddChangeListener(OnCollectionChanged);
            Volatile.Write(ref _listening, 1);
        }
        catch
        {
            _database.Dispose();
            throw;
        }
    }

    public string Name => _database.Name;

    /// <summary>The folder of the database files, such as <c>.../apexpost_courier.cblite2/</c>.</summary>
    public string? Path => _database.Path;

    /// <summary>Scans waiting to upload. Kept up to date by the change listener.</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    public bool IsDisposed => Volatile.Read(ref _disposed);

    /// <summary>True while the collection change listener is registered. It turns false only after the listener has been removed.</summary>
    internal bool HasChangeListener => Volatile.Read(ref _listening) == 1;

    /// <summary>
    /// Raised on a background thread after documents change, one notification at a time. Handlers should be quick
    /// and must not wait for another thread that is disposing this database.
    /// </summary>
    public event EventHandler<ScansChangedEventArgs>? ScansChanged;

    /// <summary>The names of the indexes on the scans collection.</summary>
    public IReadOnlyList<string> GetIndexNames()
    {
        ThrowIfDisposed();
        return [.. _collection.GetIndexes()];
    }

    internal Collection DefaultCollection
    {
        get
        {
            ThrowIfDisposed();
            return _collection;
        }
    }

    internal IQuery CreateQuery(string sql)
    {
        ThrowIfDisposed();
        return _database.CreateQuery(sql);
    }

    /// <summary>Counts scans whose syncStatus is Pending, using a query parameter for the status.</summary>
    internal int CountPending()
    {
        using var query = CreateQuery(ScanQueries.CountByStatus);
        query.Parameters = new Parameters().SetString(ScanQueries.StatusParameter, SyncStatus.Pending.ToString());
        using var results = query.Execute();
        var rows = results.AllResults();
        return rows.Count == 0 ? 0 : rows[0].GetInt("total");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // From here on a notification that arrives does nothing. Taking the lock also waited for any notification
            // that was running, so none is using the database now.
            _disposed = true;
            ScansChanged = null;
        }

        // Done outside the lock: removing the listener and closing the database may wait for Couchbase's own callbacks,
        // and a callback needs the lock to find out that it should stop.
        try
        {
            StopListening();
        }
        finally
        {
            _collection.Dispose();
            _database.Dispose();
        }
    }

    /// <summary>Removes the collection change listener using the token kept when it was registered. Dispose calls this first.</summary>
    internal void StopListening()
    {
        ListenerToken? token;
        lock (_gate)
        {
            token = _changeToken;
            _changeToken = null;
        }

        if (token is { } registered)
        {
            _collection.RemoveChangeListener(registered);
            Volatile.Write(ref _listening, 0);
        }
    }

    // Value indexes on the two fields the queries filter on. Creating an index that already exists is harmless.
    private void EnsureIndexes()
    {
        _collection.CreateIndex(TrackingNumberIndexName, new ValueIndexConfiguration(ScanFields.TrackingNumber));
        _collection.CreateIndex(SyncStatusIndexName, new ValueIndexConfiguration(ScanFields.SyncStatus));
    }

    private void OnCollectionChanged(object? sender, CollectionChangedEventArgs e)
    {
        var ids = e.DocumentIDs.ToArray();

        // Notifications can arrive on different threads, so they are handled one at a time and in order.
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // This runs on Couchbase's thread. An exception here, even one thrown by a subscriber, would end the
            // process, so it is logged and swallowed.
            try
            {
                var pending = CountPending();
                Volatile.Write(ref _pendingCount, pending);
                _log?.Invoke($"[listener] changed: {string.Join(", ", ids)}");
                ScansChanged?.Invoke(this, new ScansChangedEventArgs(ids, pending));
            }
            catch (Exception exception)
            {
                TryLog($"[listener] could not handle a change: {exception.Message}");
            }
        }
    }

    private void TryLog(string line)
    {
        try
        {
            _log?.Invoke(line);
        }
        catch (Exception)
        {
            // A failing log must not take the listener down.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
}
