using System.Collections.Concurrent;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class ChangeListenerTests
{
    [Fact]
    public void AddingAScan_RaisesTheEventWithItsIdAndThePendingCount()
    {
        using var db = new TestCourierDatabase();
        var events = Collect(db);

        var scan = db.Repository.Create(Scans.Make());

        Assert.True(Wait.Until(() => events.Any(e => e.DocumentIds.Contains(scan.Id))));
        Assert.Equal(1, events.First(e => e.DocumentIds.Contains(scan.Id)).PendingCount);
    }

    [Fact]
    public void DeletingAScan_RaisesTheEventToo()
    {
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make());
        var events = Collect(db);

        db.Repository.Delete(scan.Id);

        Assert.True(Wait.Until(() => events.Any(e => e.DocumentIds.Contains(scan.Id))));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 0));
    }

    [Fact]
    public void TheIdsOfEveryChangedDocument_AreWrittenToTheLog()
    {
        using var db = new TestCourierDatabase();

        var first = db.Repository.Create(Scans.Make("APX-1"));
        var second = db.Repository.Create(Scans.Make("APX-2"));

        Assert.True(Wait.Until(() => LogMentions(db, first.Id) && LogMentions(db, second.Id)));
        Assert.All(db.LogLines, line => Assert.StartsWith("[listener] changed: scan::", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThePendingCount_IsLive_FallingAsTheOutboxSyncsScans()
    {
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new FakeShipmentUploader());
        db.Repository.Create(Scans.Make("APX-1"));
        db.Repository.Create(Scans.Make("APX-2"));
        db.Repository.Create(Scans.Make("APX-3"));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 3));

        await outbox.SyncPendingAsync();

        Assert.True(Wait.Until(() => db.Database.PendingCount == 0));
    }

    [Fact]
    public void ManyChanges_AreAllReported()
    {
        using var db = new TestCourierDatabase();
        var events = Collect(db);

        var ids = Enumerable.Range(1, 12).Select(n => db.Repository.Create(Scans.Make($"APX-{n}")).Id).ToList();

        Assert.True(Wait.Until(() => ids.All(id => events.Any(e => e.DocumentIds.Contains(id)))));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 12));
    }

    [Fact]
    public void TheListener_IsRegisteredWhileOpen_AndRemovedOnDispose()
    {
        using var db = new TestCourierDatabase();
        Assert.True(db.Database.HasChangeListener);

        db.Database.Dispose();

        Assert.False(db.Database.HasChangeListener);
    }

    [Fact]
    public async Task OnceTheListenerIsRemoved_NoFurtherChangesAreReported()
    {
        using var db = new TestCourierDatabase();
        var events = Collect(db);
        db.Repository.Create(Scans.Make("APX-1"));
        Assert.True(Wait.Until(() => events.Count == 1));

        db.Database.StopListening();
        db.Repository.Create(Scans.Make("APX-2"));
        db.Repository.Create(Scans.Make("APX-3"));
        await Task.Delay(750);

        Assert.Single(events);
        Assert.False(db.Database.HasChangeListener);
        Assert.Equal(1, db.Database.PendingCount);
        Assert.Equal(3, db.Repository.CountPending());
    }

    [Fact]
    public void ASubscriberThatThrows_DoesNotStopTheListenerOrTheProcess()
    {
        using var db = new TestCourierDatabase();
        db.Database.ScansChanged += (_, _) => throw new InvalidOperationException("subscriber bug");

        db.Repository.Create(Scans.Make("APX-1"));
        db.Repository.Create(Scans.Make("APX-2"));

        Assert.True(Wait.Until(() => db.LogLines.Count(line => line.Contains("subscriber bug", StringComparison.Ordinal)) >= 1));
        Assert.True(Wait.Until(() => db.Database.PendingCount == 2));
    }

    [Fact]
    public async Task Dispose_WhileChangesAreStillArriving_NeitherHangsNorThrows()
    {
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
        Assert.True(Wait.Until(() => db.Database.PendingCount > 5));

        var dispose = Task.Run(db.Database.Dispose);

        Assert.Same(dispose, await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(30))));
        await dispose;
        await writer;
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
