using ApexPost.Courier.Offline.Storage;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class CourierDatabaseTests
{
    [Fact]
    public void Opens_ApexpostCourier_AndUsesTheDefaultCollection()
    {
        // Act
        using var db = new TestCourierDatabase();

        // Assert
        Assert.Equal("apexpost_courier", db.Database.Name);
        Assert.Equal("apexpost_courier", CourierDatabase.DefaultName);
        Assert.EndsWith("apexpost_courier.cblite2" + Path.DirectorySeparatorChar, db.Database.Path, StringComparison.Ordinal);
        Assert.StartsWith(db.Folder, db.Database.Path, StringComparison.Ordinal);
        Assert.Equal("_default", db.Database.DefaultCollection.Name);
        Assert.Equal("_default", db.Database.DefaultCollection.Scope.Name);
    }

    [Fact]
    public void Open_NewFolder_StartsEmpty()
    {
        // Act
        using var db = new TestCourierDatabase();

        // Assert
        Assert.Empty(db.Repository.GetAll());
        Assert.Equal(0, db.Database.PendingCount);
        Assert.False(db.Database.IsDisposed);
    }

    [Fact]
    public void Scans_SurviveClosingAndReopening()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make(notes: "left at the counter"));

        // Act
        db.Reopen();

        // Assert
        Assert.Equal(scan, db.Repository.Get(scan.Id));
        Assert.Equal(1, db.Database.PendingCount);
    }

    [Fact]
    public void Open_NewDatabase_CreatesTrackingNumberAndSyncStatusIndexes()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        var names = db.Database.GetIndexNames();

        // Assert
        Assert.Contains("idx_trackingNumber", names);
        Assert.Contains("idx_syncStatus", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void OpeningAgain_KeepsTheSameTwoIndexes()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        db.Reopen();

        // Assert
        Assert.Equal(["idx_syncStatus", "idx_trackingNumber"], db.Database.GetIndexNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Dispose_RemovesTheChangeListenerAndClosesTheDatabase()
    {
        // Act
        using var db = new TestCourierDatabase();

        // Assert
        Assert.True(db.Database.HasChangeListener);

        // Arrange (next step)
        db.Database.Dispose();

        // Act and assert
        Assert.False(db.Database.HasChangeListener);
        Assert.True(db.Database.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => db.Database.GetIndexNames());
        Assert.Throws<ObjectDisposedException>(() => db.Repository.GetAll());
        Assert.Throws<ObjectDisposedException>(() => db.Repository.Get("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301"));
    }

    [Fact]
    public void Dispose_CanBeCalledMoreThanOnce()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        db.Database.Dispose();

        // Act
        db.Database.Dispose();

        // Assert
        Assert.True(db.Database.IsDisposed);
    }

    [Fact]
    public void AfterDispose_TheFolderCanBeOpenedAgainAndNoEventsReachTheOldHandler()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var seen = 0;
        db.Database.ScansChanged += (_, _) => Interlocked.Increment(ref seen);
        db.Database.Dispose();

        using var second = new CourierDatabase(new CourierDatabaseOptions { Directory = db.Folder });
        var repository = new CouchbaseScanRepository(second);

        // Act
        repository.Create(Scans.Make());

        // Assert
        Assert.True(Wait.Until(() => second.PendingCount == 1));

        Assert.Equal(0, Volatile.Read(ref seen));
    }
}
