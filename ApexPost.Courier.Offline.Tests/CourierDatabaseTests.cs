using ApexPost.Courier.Offline.Storage;

namespace ApexPost.Courier.Offline.Tests;

public class CourierDatabaseTests
{
    [Fact]
    public void Opens_ApexpostCourier_AndUsesTheDefaultCollection()
    {
        using var db = new TestCourierDatabase();

        Assert.Equal("apexpost_courier", db.Database.Name);
        Assert.Equal("apexpost_courier", CourierDatabase.DefaultName);
        Assert.EndsWith("apexpost_courier.cblite2" + Path.DirectorySeparatorChar, db.Database.Path, StringComparison.Ordinal);
        Assert.StartsWith(db.Folder, db.Database.Path, StringComparison.Ordinal);
        Assert.Equal("_default", db.Database.DefaultCollection.Name);
        Assert.Equal("_default", db.Database.DefaultCollection.Scope.Name);
    }

    [Fact]
    public void CreatesAFreshDatabaseThatStartsEmpty()
    {
        using var db = new TestCourierDatabase();

        Assert.Empty(db.Repository.GetAll());
        Assert.Equal(0, db.Database.PendingCount);
        Assert.False(db.Database.IsDisposed);
    }

    [Fact]
    public void Scans_SurviveClosingAndReopening()
    {
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make(notes: "left at the counter"));

        db.Reopen();

        Assert.Equal(scan, db.Repository.Get(scan.Id));
        Assert.Equal(1, db.Database.PendingCount);
    }

    [Fact]
    public void CreatesTheValueIndexesOnTrackingNumberAndSyncStatus()
    {
        using var db = new TestCourierDatabase();

        var names = db.Database.GetIndexNames();

        Assert.Contains("idx_trackingNumber", names);
        Assert.Contains("idx_syncStatus", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void OpeningAgain_KeepsTheSameTwoIndexes()
    {
        using var db = new TestCourierDatabase();

        db.Reopen();

        Assert.Equal(["idx_syncStatus", "idx_trackingNumber"], db.Database.GetIndexNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Dispose_RemovesTheChangeListenerAndClosesTheDatabase()
    {
        using var db = new TestCourierDatabase();
        Assert.True(db.Database.HasChangeListener);

        db.Database.Dispose();

        Assert.False(db.Database.HasChangeListener);
        Assert.True(db.Database.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => db.Database.GetIndexNames());
        Assert.Throws<ObjectDisposedException>(() => db.Repository.GetAll());
        Assert.Throws<ObjectDisposedException>(() => db.Repository.Get("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        using var db = new TestCourierDatabase();

        db.Database.Dispose();
        db.Database.Dispose();

        Assert.True(db.Database.IsDisposed);
    }

    [Fact]
    public void AfterDispose_TheFolderCanBeOpenedAgainAndNoEventsReachTheOldHandler()
    {
        using var db = new TestCourierDatabase();
        var seen = 0;
        db.Database.ScansChanged += (_, _) => Interlocked.Increment(ref seen);
        db.Database.Dispose();

        using var second = new CourierDatabase(new CourierDatabaseOptions { Directory = db.Folder });
        var repository = new CouchbaseScanRepository(second);
        repository.Create(Scans.Make());
        Assert.True(Wait.Until(() => second.PendingCount == 1));

        Assert.Equal(0, Volatile.Read(ref seen));
    }
}
