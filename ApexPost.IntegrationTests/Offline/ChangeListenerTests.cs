using System.Collections.Concurrent;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class ChangeListenerTests
{
    [Fact]
    public void Create_NewScan_RaisesTheEventWithItsIdAndThePendingCount()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var events = Collect(db);

        // Act
        var scan = db.Repository.Create(Scans.Make());

        // Assert
        Assert.True(Wait.Until(() => events.Any(e => e.DocumentIds.Contains(scan.Id))));
        Assert.Equal(1, events.First(e => e.DocumentIds.Contains(scan.Id)).PendingCount);
    }

    [Fact]
    public void Delete_ExistingScan_RaisesTheEvent()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make());
        var events = Collect(db);

        // Act
        db.Repository.Delete(scan.Id);

        // Assert
        Assert.True(Wait.Until(() => events.Any(e => e.DocumentIds.Contains(scan.Id))));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 0));
    }

    [Fact]
    public void Listener_SeveralChanges_WritesEveryChangedIdToTheLog()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        var first = db.Repository.Create(Scans.Make("APX-1"));

        // Act
        var second = db.Repository.Create(Scans.Make("APX-2"));

        // Assert
        Assert.True(Wait.Until(() => LogMentions(db, first.Id) && LogMentions(db, second.Id)));
        Assert.All(db.LogLines, line => Assert.StartsWith("[listener] changed: scan::", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePendingCount_IsLive_FallingAsTheOutboxSyncsScans()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new FakeShipmentUploader());
        db.Repository.Create(Scans.Make("APX-1"));
        db.Repository.Create(Scans.Make("APX-2"));
        db.Repository.Create(Scans.Make("APX-3"));
        var countRoseToThree = Wait.Until(() => db.Database.PendingCount == 3);

        // Act
        await outbox.SyncPendingAsync();

        // Assert
        Assert.True(countRoseToThree);
        Assert.True(Wait.Until(() => db.Database.PendingCount == 0));
    }

    [Fact]
    public void Create_TwelveScans_ReportsEveryChange()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var events = Collect(db);

        // Act
        var ids = Enumerable.Range(1, 12).Select(n => db.Repository.Create(Scans.Make($"APX-{n}")).Id).ToList();

        // Assert
        Assert.True(Wait.Until(() => ids.All(id => events.Any(e => e.DocumentIds.Contains(id)))));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 12));
    }

    [Fact]
    public void Listener_OpenThenDispose_IsRegisteredThenRemoved()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var registeredWhileOpen = db.Database.HasChangeListener;

        // Act
        db.Database.Dispose();

        // Assert
        Assert.True(registeredWhileOpen);
        Assert.False(db.Database.HasChangeListener);
    }

    [Fact]
    public async Task Create_AfterTheListenerIsRemoved_ReportsNothing()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var events = Collect(db);
        db.Repository.Create(Scans.Make("APX-1"));
        var firstChangeReported = Wait.Until(() => events.Count == 1);
        db.Database.StopListening();
        var seenBySentinel = new System.Collections.Concurrent.ConcurrentBag<string>();
        var sentinel = db.Database.DefaultCollection.AddChangeListener((_, change) =>
        {
            foreach (var id in change.DocumentIDs)
            {
                seenBySentinel.Add(id);
            }
        });

        // Act
        var second = db.Repository.Create(Scans.Make("APX-2"));
        var third = db.Repository.Create(Scans.Make("APX-3"));

        // A second listener added for the test sees both changes. Couchbase Lite delivers notifications in order, so
        // once it has, the removed listener would have seen them too. No fixed sleep is needed.
        var delivered = Wait.Until(() => seenBySentinel.Contains(second.Id) && seenBySentinel.Contains(third.Id));
        sentinel.Remove();

        // Assert
        Assert.True(firstChangeReported);
        Assert.True(delivered);
        Assert.Single(events);
        Assert.False(db.Database.HasChangeListener);
        Assert.Equal(1, db.Database.PendingCount);
        Assert.Equal(3, db.Repository.CountPending());
    }

    [Fact]
    public void Create_SubscriberThrows_ListenerAndProcessKeepRunning()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        db.Database.ScansChanged += (_, _) => throw new InvalidOperationException("subscriber bug");

        db.Repository.Create(Scans.Make("APX-1"));

        // Act
        db.Repository.Create(Scans.Make("APX-2"));

        // Assert
        Assert.True(Wait.Until(() => db.LogLines.Count(line => line.Contains("subscriber bug", StringComparison.Ordinal)) >= 1));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 2));
    }

    [Fact]
    public async Task Dispose_WhileChangesAreStillArriving_NeitherHangsNorThrows()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var writer = Task.Run(() =>
        {
            try
            {
                for (var number = 0; number < 400; number++)
                {
                    db.Repository.Create(Scans.Make($"APX-{number}"));
                }
            }
            catch (Exception)
            {
                // Once the database is disposed, further writes are refused. That is expected here.
            }
        });

        var changesArriving = Wait.Until(() => db.Database.PendingCount > 5);

        // Act
        var dispose = Task.Run(db.Database.Dispose);
        // The 30 seconds only stop a hang from blocking the run; a working dispose finishes at once.
        var firstToFinish = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(30)));
        var disposeError = firstToFinish == dispose ? await Record.ExceptionAsync(() => dispose) : null;
        await writer;

        // Assert
        Assert.True(changesArriving);
        Assert.Same(dispose, firstToFinish);
        Assert.Null(disposeError);
        Assert.True(db.Database.IsDisposed);
        Assert.False(db.Database.HasChangeListener);
    }

    private static ConcurrentQueue<ScansChangedEventArgs> Collect(TestCourierDatabase db)
    {
        var events = new ConcurrentQueue<ScansChangedEventArgs>();
        db.Database.ScansChanged += (_, e) => events.Enqueue(e);
        return events;
    }

    private static bool LogMentions(TestCourierDatabase db, string id) =>
        db.LogLines.Any(line => line.Contains(id, StringComparison.Ordinal));
}
