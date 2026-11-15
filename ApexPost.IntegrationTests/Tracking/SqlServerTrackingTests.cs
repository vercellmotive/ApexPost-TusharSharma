extern alias api;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using ApexPost.Tracking.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ApexPost.Testing.Tracking;
using ApexPost.Testing.Api;
using ApexPost.IntegrationTests.Api;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.IntegrationTests.Tracking;

/// <summary>
/// Set APEXPOST_TEST_SQLSERVER to a server connection string to run these, as for the Week 3 tests. They create and
/// drop a disposable database. Without the variable they are reported as skipped, not passed.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "APEXPOST_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"{Variable} is not set, so no SQL Server is available.";
        }
    }
}

public sealed class TrackingTestDatabase : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlServerFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"ApexPostTrackingTest_{Guid.NewGuid():N}" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString.Length > 0)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    public TrackingDbContext CreateContext() => new(new DbContextOptionsBuilder<TrackingDbContext>()
        .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable(TrackingDbContext.MigrationsHistoryTable, TrackingDbContext.Schema))
        .Options);

    public ShipmentService CreateService(TrackingDbContext db, TimeProvider clock) => new(new EfShipmentStore(db), new FakeBranches(), clock);
}

public sealed class SqlServerTrackingTests(TrackingTestDatabase database) : IClassFixture<TrackingTestDatabase>
{
    private readonly FakeTimeProvider _clock = TestTime.NewClock();

    private int _shipmentsCreated;

    // A tracking number per test, derived from its name: stable on every run and with no counter shared between tests.
    private static string TrackingNumberFor(string test) =>
        $"APX-5{BitConverter.ToUInt32(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(test))) % 10_000_000:D7}";

    private async Task<string> CreateShipmentAsync(string city = "Sydney", [System.Runtime.CompilerServices.CallerMemberName] string test = "")
    {
        // xUnit makes a new instance for every test, so this count belongs to one test only.
        var trackingNumber = TrackingNumberFor($"{test}#{++_shipmentsCreated}");
        await using var db = database.CreateContext();
        // Created at the start, an hour before "now", so scans made in between are neither early nor in the future.
        var result = await database.CreateService(db, new FakeTimeProvider(TestTime.Start)).CreateAsync(new NewShipment(trackingNumber, "B001", Make.Recipient(city), 1.25m));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Creating test shipment {trackingNumber} failed: {result.Error?.Detail}");
        }

        return trackingNumber;
    }

    [SqlServerFact]
    public async Task Migration_FreshDatabase_CreatesTrackingSchemaBesideWeek3()
    {
        // Arrange
        await using var db = database.CreateContext();

        // Act
        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES ORDER BY 1")
            .ToListAsync();

        // Assert
        Assert.Equal(["tracking.__EFMigrationsHistory", "tracking.Scans", "tracking.Shipments"], tables);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [SqlServerFact]
    public async Task SaveChanges_RacingDuplicate_ThrowsDuplicateValueException()
    {
        // Arrange
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var store = new EfShipmentStore(db);

        store.Add(Shipment.Create(trackingNumber, "B001", Make.Recipient(), 1m, _clock.GetUtcNow()));

        // Act
        Func<Task> act = () => store.SaveChangesAsync();

        // Assert
        await Assert.ThrowsAsync<DuplicateValueException>(act);
    }

    [SqlServerFact]
    public async Task SaveChanges_LostUpdate_ThrowsConcurrencyConflict()
    {
        // Arrange
        var trackingNumber = await CreateShipmentAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstService = database.CreateService(first, _clock);
        var secondStore = new EfShipmentStore(second);
        // The second context reads the shipment, then the first moves it on before the second saves.
        var stale = await secondStore.FindAsync(trackingNumber);
        var firstMove = await firstService.ChangeStatusAsync(trackingNumber, ShipmentStatus.PickedUp);
        stale!.MoveTo(ShipmentStatus.PickedUp, _clock.GetUtcNow());

        // Act
        Func<Task> act = () => secondStore.SaveChangesAsync();

        // Assert
        Assert.True(firstMove.Succeeded);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(act);
    }

    [SqlServerFact]
    public async Task UploadScans_ParallelSameBatch_StoresEachScanOnce()
    {
        // Arrange
        var trackingNumber = await CreateShipmentAsync();
        var batch = Enumerable.Range(0, 20).Select(index => Make.Scan(at: TestTime.Start.AddMinutes(index))).ToList();

        // Act
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = database.CreateContext();
            return (await database.CreateService(db, _clock).UploadScansAsync(trackingNumber, batch, Make.CourierId)).Value!;
        }));

        // Assert
        Assert.Equal(20, outcomes.Sum(outcome => outcome.Accepted));
        Assert.Equal(60, outcomes.Sum(outcome => outcome.DuplicatesSkipped));
        Assert.All(outcomes, outcome => Assert.Equal(0, outcome.Rejected));
        await using var check = database.CreateContext();
        var shipment = await check.Shipments.SingleAsync(item => item.TrackingNumber == trackingNumber);
        Assert.Equal(20, await check.Scans.CountAsync(scan => scan.ShipmentId == shipment.Id));
    }

    [SqlServerFact]
    public async Task ListAsync_CityInOtherCase_FiltersAndCarriesLatestScan()
    {
        // Arrange
        var city = $"City{Guid.NewGuid():N}"[..20];
        var withScans = await CreateShipmentAsync(city);
        var withoutScans = await CreateShipmentAsync(city);
        await using (var db = database.CreateContext())
        {
            await database.CreateService(db, _clock).UploadScansAsync(withScans, [
                Make.Scan(type: ScanType.Pickup, at: TestTime.Start.AddMinutes(5)),
                Make.Scan(type: ScanType.InTransit, at: TestTime.Start.AddMinutes(25)),
            ], Make.CourierId);
        }

        await using var read = database.CreateContext();

        // Act
        var page = await new EfShipmentStore(read).ListAsync(new ShipmentQuery(1, 50, ShipmentStatus.Created, city.ToUpperInvariant()));

        // Assert
        Assert.Equal(2, page.TotalCount);
        var views = page.Items.ToDictionary(view => view.TrackingNumber);
        Assert.Equal(ScanType.InTransit, views[withScans].LatestScan?.ScanType);
        Assert.Null(views[withoutScans].LatestScan);
    }

    [SqlServerFact]
    public async Task Remove_ShipmentWithScans_DeletesItsScans()
    {
        // Arrange
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var service = database.CreateService(db, _clock);
        await service.UploadScansAsync(trackingNumber, [Make.Scan()], Make.CourierId);

        // Act
        var deleted = await service.DeleteAsync(trackingNumber);

        // Assert
        Assert.True(deleted.Succeeded);
        Assert.False(await db.Shipments.AnyAsync(item => item.TrackingNumber == trackingNumber));
        Assert.Equal(0, await db.Scans.CountAsync(scan => !db.Shipments.Any(shipment => shipment.Id == scan.ShipmentId)));
    }

    [SqlServerFact]
    public async Task CheckConstraint_DeliveredScanWithoutSignature_IsRejected()
    {
        // Arrange
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var shipment = await db.Shipments.SingleAsync(item => item.TrackingNumber == trackingNumber);
        db.Scans.Add(TrackingScan.Record(Guid.NewGuid(), shipment.Id, ScanType.Delivered, "CR-1", "Door", _clock.GetUtcNow(), null, null, _clock.GetUtcNow()));

        // Act
        var exception = await Record.ExceptionAsync(() => db.SaveChangesAsync());

        // Assert
        var error = Assert.IsType<DbUpdateException>(exception);
        Assert.Contains("CK_Scans_DeliveredSignature", error.InnerException?.Message);
    }

    [SqlServerFact]
    public async Task Api_EndToEndAgainstSqlServer_Succeeds()
    {
        // Arrange
        await using var factory = new WebApplicationFactory<api::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:ApexPostDb", database.ConnectionString);
            ApiFactory.UseTestSecurity(builder);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IBranchRepository, FakeBranches>();
                services.AddSingleton<TimeProvider>(_clock);
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token(ApexPostRoles.Admin, ApexPostRoles.Courier));
        var trackingNumber = TrackingNumberFor(nameof(Api_EndToEndAgainstSqlServer_Succeeds));

        var created = await client.PostAsJsonAsync("/api/v2/shipments", ShipmentEndpointTests.NewShipment(trackingNumber));
        var batch = new { scans = new[] { new { scanId = Make.NewScanId(), scanType = "Pickup", courierId = "CR-1001", location = "Depot", scannedAtUtc = _clock.GetUtcNow() } } };
        var first = await client.PostAsJsonAsync($"/api/v2/shipments/{trackingNumber}/scans", batch);
        var again = await client.PostAsJsonAsync($"/api/v2/shipments/{trackingNumber}/scans", batch);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v2/shipments/{trackingNumber}/scans");

        // Act
        var duplicate = await client.PostAsJsonAsync("/api/v2/shipments", ShipmentEndpointTests.NewShipment(trackingNumber));

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetInt32());
        Assert.Equal(1, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(2.5m, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("weightKg").GetDecimal());
    }
}
