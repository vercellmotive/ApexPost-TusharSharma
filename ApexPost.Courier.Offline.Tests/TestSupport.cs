using System.Collections.Concurrent;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using Couchbase.Lite.Logging;

namespace ApexPost.Courier.Offline.Tests;

/// <summary>A real embedded database in a temporary folder, deleted when the test ends. No mocks, no network.</summary>
internal sealed class TestCourierDatabase : IDisposable
{
    static TestCourierDatabase() => LogSinks.Console = new ConsoleLogSink(LogLevel.None);

    private readonly ConcurrentQueue<string> _log = new();

    public TestCourierDatabase()
    {
        Folder = Path.Combine(Path.GetTempPath(), "apexpost-courier-tests", Guid.NewGuid().ToString("N"));
        Database = OpenCore();
        Repository = new CouchbaseScanRepository(Database);
    }

    public string Folder { get; }

    public CourierDatabase Database { get; private set; }

    public CouchbaseScanRepository Repository { get; private set; }

    /// <summary>Every line the change listener logged.</summary>
    public IReadOnlyList<string> LogLines => [.. _log];

    /// <summary>Closes the database and opens the same folder again, as the app does after a restart.</summary>
    public void Reopen()
    {
        Database.Dispose();
        Database = OpenCore();
        Repository = new CouchbaseScanRepository(Database);
    }

    public void Dispose()
    {
        Database.Dispose();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that cannot be removed right now is not a test failure.
        }
    }

    private CourierDatabase OpenCore() =>
        new(new CourierDatabaseOptions { Directory = Folder, Log = _log.Enqueue });
}

internal static class Wait
{
    /// <summary>Polls until the condition is true. Change notifications arrive on another thread, so tests wait for them.</summary>
    public static bool Until(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }
}

internal static class Scans
{
    public static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public static ShipmentScan Make(
        string trackingNumber = "APX-20260001",
        ScanType scanType = ScanType.Pickup,
        string courierId = "C-1001",
        DateTimeOffset? at = null,
        string? notes = null,
        string location = "Sydney NSW") =>
        ShipmentScan.CreatePending(trackingNumber, scanType, courierId, location, at ?? Noon, notes);
}
