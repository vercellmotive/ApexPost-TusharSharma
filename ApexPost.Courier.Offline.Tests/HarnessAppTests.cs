using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Harness;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Offline.Tests;

public class HarnessAppTests
{
    [Fact]
    public async Task TheMenu_ListsTheSixOptions_AndNamesTheDatabase()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "6");

        foreach (var item in new[] { "1) Add Scan", "2) Find by Tracking Number", "3) List Pending", "4) Simulate Sync", "5) Delete Scan", "6) Exit" })
        {
            Assert.Contains(item, output, StringComparison.Ordinal);
        }

        Assert.Contains("apexpost_courier", output, StringComparison.Ordinal);
        Assert.Contains("Closing the database. Goodbye.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddScan_SavesItPending_AndShowsTheCouriersCounts()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "1", "APX-9", "1", "C-7", "Sydney NSW", "", "6");

        Assert.Contains("on this device as Pending", output, StringComparison.Ordinal);
        Assert.Contains("Courier C-7 has 1 scan(s): Pickup 1, InTransit 0, Delivered 0, Failed 0.", output, StringComparison.Ordinal);
        var saved = Assert.Single(db.Repository.GetAll());
        Assert.Equal(SyncStatus.Pending, saved.SyncStatus);
        Assert.Equal("APX-9", saved.TrackingNumber);
    }

    [Fact]
    public async Task AddScan_AcceptsTheScanTypeByNameAsWellAsByNumber()
    {
        using var db = new TestCourierDatabase();

        await RunAsync(db, "1", "APX-1", "in transit", "C-1", "Perth WA", "", "1", "APX-1", "3", "C-1", "Perth WA", "", "6");

        Assert.Equal([ScanType.InTransit, ScanType.Delivered], db.Repository.GetAll().Select(scan => scan.ScanType));
    }

    [Fact]
    public async Task FindByTrackingNumber_ShowsItsScansOldestFirst()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "1", "APX-9", "1", "C-7", "Depot", "", "1", "APX-9", "2", "C-7", "Van 12", "on the road", "1", "APX-8", "1", "C-7", "Depot", "", "2", "APX-9", "6");

        Assert.Contains("2 scan(s) for APX-9, oldest first:", output, StringComparison.Ordinal);
        var listing = output[output.IndexOf("2 scan(s) for APX-9", StringComparison.Ordinal)..];
        Assert.True(listing.IndexOf("Pickup", StringComparison.Ordinal) < listing.IndexOf("InTransit", StringComparison.Ordinal));
        Assert.DoesNotContain("APX-8", listing[..listing.IndexOf("=== ApexPost", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("note: on the road", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindByTrackingNumber_SaysSoWhenThereAreNoScans()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "2", "APX-NONE", "6");

        Assert.Contains("No scans found for APX-NONE.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SimulateSync_SyncsAcceptedScans_FailsRejectedOnes_AndListPendingThenShowsOnlyNewWork()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "1", "APX-1", "1", "C-1", "Depot", "", "1", "APX-2", "1", "C-1", "Depot", "[fail] torn label", "3", "4", "1", "APX-3", "1", "C-1", "Depot", "", "3", "6");

        Assert.Contains("2 scan(s) waiting to sync, oldest first:", output, StringComparison.Ordinal);
        Assert.Contains("Done: 1 synced, 1 failed, 0 skipped.", output, StringComparison.Ordinal);
        Assert.Contains("1 scan(s) waiting to sync, oldest first:", output, StringComparison.Ordinal);
        Assert.Equal(
            [SyncStatus.Synced, SyncStatus.Failed, SyncStatus.Pending],
            db.Repository.GetAll().Select(scan => scan.SyncStatus));
    }

    [Fact]
    public async Task SimulateSync_WithNothingPending_SaysSo()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "4", "3", "6");

        Assert.Contains("Nothing is waiting to sync.", output, StringComparison.Ordinal);
        Assert.Contains("No scans are waiting to sync.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteScan_RemovesTheScanWhoseIdIsTyped()
    {
        using var db = new TestCourierDatabase();
        var script = new ScriptedReader("1", "APX-1", "1", "C-1", "Depot", "", "5", () => db.Repository.GetAll()[0].Id, "6");

        var output = await RunAsync(db, script);

        Assert.Contains("Deleted scan::", output, StringComparison.Ordinal);
        Assert.Empty(db.Repository.GetAll());
    }

    [Fact]
    public async Task DeleteScan_ReportsABadIdAndAnUnknownId_WithoutDeletingAnything()
    {
        using var db = new TestCourierDatabase();
        db.Repository.Create(Scans.Make());
        var unknown = ShipmentScan.NewId();

        var output = await RunAsync(db, "5", "not-an-id", "5", unknown, "6");

        Assert.Contains("That is not a scan id.", output, StringComparison.Ordinal);
        Assert.Contains($"No scan has the id {unknown}.", output, StringComparison.Ordinal);
        Assert.Single(db.Repository.GetAll());
    }

    [Fact]
    public async Task DeleteScan_WithNoScans_SaysThereIsNothingToDelete()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "5", "6");

        Assert.Contains("There are no scans to delete.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABadMenuChoiceABadScanTypeAndABadTrackingNumber_AreReported_AndTheMenuCarriesOn()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "9", "1", "APX-1", "7", "C-1", "Depot", "", "1", "bad id", "1", "C-1", "Depot", "", "6");

        Assert.Contains("Please choose a number from 1 to 6.", output, StringComparison.Ordinal);
        Assert.Contains("That is not a scan type.", output, StringComparison.Ordinal);
        Assert.Contains("Could not do that:", output, StringComparison.Ordinal);
        Assert.Empty(db.Repository.GetAll());
        Assert.Contains("Goodbye.", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhenTheInputEndsInTheMiddleOfAScan_TheHarnessStopsCleanlyAndSavesNothing()
    {
        using var db = new TestCourierDatabase();

        var output = await RunAsync(db, "1", "APX-1", "1");

        Assert.DoesNotContain("Goodbye.", output, StringComparison.Ordinal);
        Assert.Empty(db.Repository.GetAll());
    }

    [Fact]
    public async Task TheLivePendingCount_IsPrintedAfterTheListenerFires()
    {
        using var db = new TestCourierDatabase();
        var script = new ScriptedReader("1", "APX-1", "1", "C-1", "Depot", "", () =>
        {
            Wait.Until(() => db.Database.PendingCount == 1);
            Thread.Sleep(200);
            return "6";
        });

        var output = await RunAsync(db, script);

        Assert.Contains("[pending] 1 scan(s) waiting to sync", output, StringComparison.Ordinal);
    }

    private static Task<string> RunAsync(TestCourierDatabase db, params string[] lines) =>
        RunAsync(db, new ScriptedReader(lines.Cast<object>().ToArray()));

    private static async Task<string> RunAsync(TestCourierDatabase db, ScriptedReader input)
    {
        var outbox = new OutboxService(db.Repository, new FakeShipmentUploader());
        var output = new StringWriter();
        var app = new HarnessApp(db.Database, db.Repository, outbox, input, output, echoInput: true, new SteppingClock());

        Assert.Equal(0, await app.RunAsync());

        // A change notification may still be on its way, so let it finish before reading the text.
        await Task.Delay(300);
        lock (output)
        {
            return output.ToString();
        }
    }

    /// <summary>Plays back a script. An entry is either a line of text or a function that produces the line when it is read.</summary>
    private sealed class ScriptedReader : TextReader
    {
        private readonly Queue<object> _script;

        public ScriptedReader(params object[] entries) => _script = new Queue<object>(entries);

        public override string? ReadLine() =>
            _script.Count == 0
                ? null
                : _script.Dequeue() switch
                {
                    Func<string> produce => produce(),
                    var text => (string)text
                };
    }

    /// <summary>A clock that moves forward one minute on every reading, so scans made in a test have distinct, ordered times.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = Scans.Noon;

        public override DateTimeOffset GetUtcNow()
        {
            lock (this)
            {
                _now = _now.AddMinutes(1);
                return _now;
            }
        }
    }
}
