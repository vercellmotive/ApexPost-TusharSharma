using ApexPost.Courier.Offline.Domain;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class CouchbaseScanRepositoryTests
{
    [Fact]
    public void Create_ThenGetById_ReturnsTheSameScan()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var created = db.Repository.Create(Scans.Make(notes: "ring twice"));

        // Act
        var read = db.Repository.Get(created.Id);

        // Assert
        Assert.Equal(created, read);
        Assert.Equal(SyncStatus.Pending, read!.SyncStatus);
    }

    [Fact]
    public void Create_KeepsUnicodeQuotesAndExactTime()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var at = new DateTimeOffset(2026, 11, 2, 8, 30, 15, TimeSpan.Zero).AddTicks(1234567);
        var created = db.Repository.Create(Scans.Make(at: at, location: "Zürich — 東京 🚚", notes: "said \"leave it\" with 'Sam'"));

        // Act
        var read = db.Repository.Get(created.Id)!;

        // Assert
        Assert.Equal("Zürich — 東京 🚚", read.Location);
        Assert.Equal("said \"leave it\" with 'Sam'", read.Notes);
        Assert.Equal(at, read.ScannedAtUtc);
    }

    [Fact]
    public void Create_RejectsAScanThatIsNotPending()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        var synced = Scans.Make() with { SyncStatus = SyncStatus.Synced };

        // Act and assert
        Assert.Throws<ArgumentException>(() => db.Repository.Create(synced));
        Assert.Null(db.Repository.Get(synced.Id));
    }

    [Fact]
    public void Create_RejectsADuplicateId_AndKeepsTheOriginal()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var original = db.Repository.Create(Scans.Make(notes: "original"));

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => db.Repository.Create(original with { Notes = "overwrite attempt" }));

        Assert.Equal("original", db.Repository.Get(original.Id)!.Notes);
        Assert.Single(db.Repository.GetAll());
    }

    [Fact]
    public void Create_RejectsAnInvalidScan()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act and assert
        Assert.Throws<ArgumentException>(() => db.Repository.Create(Scans.Make() with { Id = "scan::oops" }));
        Assert.Throws<ArgumentException>(() => db.Repository.Create(Scans.Make() with { TrackingNumber = "has space" }));
        Assert.Throws<ArgumentNullException>(() => db.Repository.Create(null!));
        Assert.Empty(db.Repository.GetAll());
    }

    [Fact]
    public void Get_ReturnsNull_ForAnIdThatDoesNotExist()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act and assert
        Assert.Null(db.Repository.Get(ShipmentScan.NewId()));
        Assert.Throws<ArgumentException>(() => db.Repository.Get(" "));
    }

    [Fact]
    public void Update_ReplacesEveryField()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var original = db.Repository.Create(Scans.Make("APX-1", ScanType.Pickup, "C-1", notes: "first"));

        // Act
        var changed = original with
        {
            TrackingNumber = "APX-2",
            ScanType = ScanType.Failed,
            CourierId = "C-2",
            Location = "Hobart TAS",
            ScannedAtUtc = original.ScannedAtUtc.AddHours(1),
            Notes = null,
            SyncStatus = SyncStatus.Failed
        };

        // Assert
        Assert.True(db.Repository.Update(changed));

        Assert.Equal(changed, db.Repository.Get(original.Id));
    }

    [Fact]
    public void Update_ReturnsFalse_AndDoesNotRecreateAScanThatIsGone()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        var gone = Scans.Make();

        // Assert
        Assert.False(db.Repository.Update(gone));
        Assert.Null(db.Repository.Get(gone.Id));
    }

    [Fact]
    public void UpdateSyncStatus_ChangesOnlyTheStatus_AndKeepsOtherEdits()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var scan = db.Repository.Create(Scans.Make(notes: "before"));

        // Act
        db.Repository.Update(scan with { Notes = "edited meanwhile" });

        // Assert
        Assert.True(db.Repository.UpdateSyncStatus(scan.Id, SyncStatus.Synced));

        // Act
        var read = db.Repository.Get(scan.Id)!;

        // Assert
        Assert.Equal(SyncStatus.Synced, read.SyncStatus);
        Assert.Equal("edited meanwhile", read.Notes);
        Assert.Equal(scan.TrackingNumber, read.TrackingNumber);
    }

    [Fact]
    public void UpdateSyncStatus_ReturnsFalse_ForAScanThatIsGone()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act and assert
        Assert.False(db.Repository.UpdateSyncStatus(ShipmentScan.NewId(), SyncStatus.Synced));
        Assert.Throws<ArgumentOutOfRangeException>(() => db.Repository.UpdateSyncStatus(ShipmentScan.NewId(), (SyncStatus)42));
    }

    [Fact]
    public void Delete_RemovesTheScan_AndReturnsFalseTheSecondTime()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        var scan = db.Repository.Create(Scans.Make());

        // Assert
        Assert.True(db.Repository.Delete(scan.Id));
        Assert.Null(db.Repository.Get(scan.Id));
        Assert.Empty(db.Repository.GetAll());
        Assert.False(db.Repository.Delete(scan.Id));
        Assert.False(db.Repository.Delete(ShipmentScan.NewId()));
    }

    [Fact]
    public void GetAll_ListsEveryScan_OldestFirst()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var third = db.Repository.Create(Scans.Make("APX-3", at: Scans.Noon.AddMinutes(30)));
        var first = db.Repository.Create(Scans.Make("APX-1", at: Scans.Noon));
        var second = db.Repository.Create(Scans.Make("APX-2", at: Scans.Noon.AddMinutes(10)));

        // Act
        var all = db.Repository.GetAll();

        // Assert
        Assert.Equal([first.Id, second.Id, third.Id], all.Select(scan => scan.Id));
    }
}
