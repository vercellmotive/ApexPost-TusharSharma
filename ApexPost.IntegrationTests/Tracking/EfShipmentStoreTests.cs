using ApexPost.Testing.Persistence;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using ApexPost.Tracking.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.IntegrationTests.Tracking;

/// <summary>
/// R2 and R3 at the repository level: the real <see cref="EfShipmentStore"/> and <see cref="TrackingDbContext"/> on
/// SQLite in memory. The LINQ is translated to SQL and run, so a query the provider cannot translate, a wrong filter,
/// a wrong order or a broken projection fails here, which a mocked DbSet would never show. Every test has its own
/// database, so no test can see another's rows.
/// </summary>
public sealed class EfShipmentStoreTests : IDisposable
{
    private readonly SqliteTrackingDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<Shipment> SeedAsync(string trackingNumber, DateTimeOffset createdAt, string city = "Sydney", ShipmentStatus status = ShipmentStatus.Created)
    {
        await using var db = _database.CreateContext();
        var shipment = Make.Shipment(trackingNumber, createdAt, city);
        foreach (var step in new[] { ShipmentStatus.PickedUp, ShipmentStatus.InTransit }.Where(step => step <= status))
        {
            shipment.MoveTo(step, createdAt);
        }

        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();
        return shipment;
    }

    private async Task SeedScansAsync(int shipmentId, params (ScanType Type, DateTimeOffset At)[] scans)
    {
        await using var db = _database.CreateContext();
        db.Scans.AddRange(scans.Select(scan => TrackingScan.Record(
            Guid.NewGuid(), shipmentId, scan.Type, Make.CourierId, $"{scan.Type} stop", scan.At, null,
            scan.Type == ScanType.Delivered ? "Mia Fictional" : null, scan.At)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListAsync_StatusAndCityFilter_ReturnsOnlyMatchesNewestFirst()
    {
        // Arrange
        await SeedAsync("APX-10000001", TestTime.Start, "Sydney", ShipmentStatus.InTransit);
        await SeedAsync("APX-10000002", TestTime.Start.AddHours(1), "Sydney", ShipmentStatus.InTransit);
        await SeedAsync("APX-10000003", TestTime.Start.AddHours(2), "Perth", ShipmentStatus.InTransit);
        await SeedAsync("APX-10000004", TestTime.Start.AddHours(3), "Sydney", ShipmentStatus.Created);
        await using var db = _database.CreateContext();
        var store = new EfShipmentStore(db);

        // Act
        var page = await store.ListAsync(new ShipmentQuery(1, 20, ShipmentStatus.InTransit, "Sydney"));

        // Assert
        Assert.Equal(["APX-10000002", "APX-10000001"], page.Items.Select(item => item.TrackingNumber));
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task ListAsync_SecondPage_SkipsTheFirstPageAndReportsTheTotal()
    {
        // Arrange
        for (var index = 1; index <= 5; index++)
        {
            await SeedAsync($"APX-1000000{index}", TestTime.Start.AddMinutes(index));
        }

        await using var db = _database.CreateContext();
        var store = new EfShipmentStore(db);

        // Act
        var page = await store.ListAsync(new ShipmentQuery(2, 2, null, null));

        // Assert
        Assert.Equal(["APX-10000003", "APX-10000002"], page.Items.Select(item => item.TrackingNumber));
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task GetViewAsync_ShipmentWithScans_ProjectsTheLatestScanInTheSameQuery()
    {
        // Arrange
        var shipment = await SeedAsync("APX-10000001", TestTime.Start);
        await SeedScansAsync(
            shipment.Id,
            (ScanType.Pickup, TestTime.Start.AddMinutes(10)),
            (ScanType.InTransit, TestTime.Start.AddMinutes(40)),
            (ScanType.Pickup, TestTime.Start.AddMinutes(20)));
        await using var db = _database.CreateContext();

        // Act
        var view = await new EfShipmentStore(db).GetViewAsync("APX-10000001");

        // Assert
        Assert.NotNull(view);
        Assert.Equal(ScanType.InTransit, view.LatestScan?.ScanType);
        Assert.Equal(TestTime.Start.AddMinutes(40), view.LatestScan?.ScannedAtUtc);
    }

    [Fact]
    public async Task GetViewAsync_NoScansYet_HasNoLatestScan()
    {
        // Arrange
        await SeedAsync("APX-10000001", TestTime.Start);
        await using var db = _database.CreateContext();

        // Act
        var view = await new EfShipmentStore(db).GetViewAsync("APX-10000001");

        // Assert
        Assert.NotNull(view);
        Assert.Null(view.LatestScan);
    }

    [Fact]
    public async Task GetScansAsync_ScansStoredOutOfOrder_ReturnsThemOldestFirst()
    {
        // Arrange
        var shipment = await SeedAsync("APX-10000001", TestTime.Start);
        await SeedScansAsync(
            shipment.Id,
            (ScanType.InTransit, TestTime.Start.AddMinutes(30)),
            (ScanType.Pickup, TestTime.Start.AddMinutes(5)));
        await using var db = _database.CreateContext();

        // Act
        var scans = await new EfShipmentStore(db).GetScansAsync(shipment.Id);

        // Assert
        Assert.Equal([ScanType.Pickup, ScanType.InTransit], scans.Select(scan => scan.ScanType));
    }

    [Fact]
    public async Task FindScanShipmentsAsync_StoredAndUnknownIds_ReturnsOnlyTheStoredOnesWithTheirShipment()
    {
        // Arrange
        var shipment = await SeedAsync("APX-10000001", TestTime.Start);
        var stored = Guid.NewGuid();
        await using (var seed = _database.CreateContext())
        {
            seed.Scans.Add(TrackingScan.Record(stored, shipment.Id, ScanType.Pickup, Make.CourierId, "Depot", TestTime.Start, null, null, TestTime.Start));
            await seed.SaveChangesAsync();
        }

        await using var db = _database.CreateContext();

        // Act
        var owners = await new EfShipmentStore(db).FindScanShipmentsAsync([stored, Guid.NewGuid()]);

        // Assert
        var owner = Assert.Single(owners);
        Assert.Equal(stored, owner.Key);
        Assert.Equal(shipment.Id, owner.Value);
    }

    [Fact]
    public async Task UploadScansAsync_SameBatchTwiceThroughTheService_StoresEachScanOnce()
    {
        // Arrange
        await SeedAsync("APX-10000001", TestTime.Start);
        IncomingScan[] batch = [Make.Scan(), Make.Scan(type: ScanType.InTransit)];

        // Act
        ScanUploadOutcome first, second;
        await using (var db = _database.CreateContext())
        {
            first = (await new ShipmentService(new EfShipmentStore(db), new FakeBranches(), TestTime.NewClock())
                .UploadScansAsync("APX-10000001", batch, Make.CourierId)).Value!;
        }

        await using (var db = _database.CreateContext())
        {
            second = (await new ShipmentService(new EfShipmentStore(db), new FakeBranches(), TestTime.NewClock())
                .UploadScansAsync("APX-10000001", batch, Make.CourierId)).Value!;
        }

        // Assert
        await using var check = _database.CreateContext();
        Assert.Equal(2, first.Accepted);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(2, second.DuplicatesSkipped);
        Assert.Equal(2, await check.Scans.CountAsync());
    }

    [Fact]
    public async Task Remove_ShipmentWithScans_CascadesToItsScans()
    {
        // Arrange
        var shipment = await SeedAsync("APX-10000001", TestTime.Start);
        await SeedScansAsync(shipment.Id, (ScanType.Pickup, TestTime.Start.AddMinutes(5)));
        await using var db = _database.CreateContext();
        var store = new EfShipmentStore(db);
        var loaded = await store.FindAsync("APX-10000001");

        // Act
        store.Remove(loaded!);
        await store.SaveChangesAsync();

        // Assert
        await using var check = _database.CreateContext();
        Assert.False(await new EfShipmentStore(check).ExistsAsync("APX-10000001"));
        Assert.Equal(0, await check.Scans.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_SecondShipmentWithSameTrackingNumber_IsRefusedByTheUniqueIndex()
    {
        // Arrange
        await SeedAsync("APX-10000001", TestTime.Start);
        await using var db = _database.CreateContext();
        var store = new EfShipmentStore(db);
        store.Add(Make.Shipment("APX-10000001", TestTime.Start.AddHours(1)));

        // Act
        Func<Task> act = () => store.SaveChangesAsync();

        // Assert
        // The index refuses the row on SQLite too. Turning SQL Server's error 2601/2627 into DuplicateValueException
        // is SQL Server-specific, so that mapping is proved by SqlServerTrackingTests, not here.
        await Assert.ThrowsAsync<DbUpdateException>(act);
    }
}
