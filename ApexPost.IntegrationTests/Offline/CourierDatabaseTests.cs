using ApexPost.Courier.Offline.Storage;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class CourierDatabaseTests
{
    [Fact]
    public void Open_NewDatabase_IsApexpostCourierWithTheDefaultCollection()
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
    public void Reopen_AfterSavingScans_KeepsTheScans()
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
    public void Reopen_ExistingDatabase_KeepsTheSameTwoIndexes()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        db.Reopen();

        // Assert
        Assert.Equal(["idx_syncStatus", "idx_trackingNumber"], db.Database.GetIndexNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Dispose_OpenDatabase_RemovesTheListenerAndCloses()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var listenerWhileOpen = db.Database.HasChangeListener;

        // Act
        db.Database.Dispose();
        Action readIndexes = () => db.Database.GetIndexNames();
        Action readAll = () => db.Repository.GetAll();
        Action readOne = () => db.Repository.Get("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301");

        // Assert
        Assert.True(listenerWhileOpen);
        Assert.False(db.Database.HasChangeListener);
        Assert.True(db.Database.IsDisposed);
        Assert.Throws<ObjectDisposedException>(readIndexes);
        Assert.Throws<ObjectDisposedException>(readAll);
        Assert.Throws<ObjectDisposedException>(readOne);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
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
    public void Dispose_ThenReopenTheSameFolder_OpensAndTheOldHandlerGetsNoEvents()
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
