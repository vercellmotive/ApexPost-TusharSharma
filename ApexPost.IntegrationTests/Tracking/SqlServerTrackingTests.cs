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
    private static int _next = 50000000;
    private readonly TestClock _clock = new();

    private static string NextTrackingNumber() => $"APX-{Interlocked.Increment(ref _next)}";

    private async Task<string> CreateShipmentAsync(string city = "Sydney")
    {
        var trackingNumber = NextTrackingNumber();
        await using var db = database.CreateContext();
        // Created at the start, an hour before "now", so scans made in between are neither early nor in the future.
        var result = await database.CreateService(db, new TestClock(TestClock.Start)).CreateAsync(new NewShipment(trackingNumber, "B001", Make.Recipient(city), 1.25m));
        Assert.True(result.Succeeded, result.Error?.Detail);
        return trackingNumber;
    }

    [SqlServerFact]
    public async Task The_migration_creates_the_tracking_schema_beside_week_3()
    {
        await using var db = database.CreateContext();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES ORDER BY 1")
            .ToListAsync();

        Assert.Equal(["tracking.__EFMigrationsHistory", "tracking.Scans", "tracking.Shipments"], tables);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [SqlServerFact]
    public async Task The_unique_index_turns_a_racing_duplicate_into_DuplicateValueException()
    {
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var store = new EfShipmentStore(db);

        store.Add(Shipment.Create(trackingNumber, "B001", Make.Recipient(), 1m, _clock.Now));

        await Assert.ThrowsAsync<DuplicateValueException>(() => store.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task The_row_version_turns_a_lost_update_into_a_conflict()
    {
        var trackingNumber = await CreateShipmentAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstService = database.CreateService(first, _clock);
        var secondStore = new EfShipmentStore(second);
        var stale = await secondStore.FindAsync(trackingNumber);

        Assert.True((await firstService.ChangeStatusAsync(trackingNumber, ShipmentStatus.PickedUp)).Succeeded);
        stale!.MoveTo(ShipmentStatus.PickedUp, _clock.Now);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondStore.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Parallel_uploads_of_one_batch_store_each_scan_exactly_once()
    {
        var trackingNumber = await CreateShipmentAsync();
        var batch = Enumerable.Range(0, 20).Select(index => Make.Scan(at: TestClock.Start.AddMinutes(index))).ToList();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = database.CreateContext();
            return (await database.CreateService(db, _clock).UploadScansAsync(trackingNumber, batch, Make.CourierId)).Value!;
        }));

        Assert.Equal(20, outcomes.Sum(outcome => outcome.Accepted));
        Assert.Equal(60, outcomes.Sum(outcome => outcome.DuplicatesSkipped));
        Assert.All(outcomes, outcome => Assert.Equal(0, outcome.Rejected));
        await using var check = database.CreateContext();
        var shipment = await check.Shipments.SingleAsync(item => item.TrackingNumber == trackingNumber);
        Assert.Equal(20, await check.Scans.CountAsync(scan => scan.ShipmentId == shipment.Id));
    }

    [SqlServerFact]
    public async Task List_filters_case_insensitively_and_carries_the_latest_scan()
    {
        var city = $"City{Guid.NewGuid():N}"[..20];
        var withScans = await CreateShipmentAsync(city);
        var withoutScans = await CreateShipmentAsync(city);
        await using (var db = database.CreateContext())
        {
            await database.CreateService(db, _clock).UploadScansAsync(withScans, [
                Make.Scan(type: ScanType.Pickup, at: TestClock.Start.AddMinutes(5)),
                Make.Scan(type: ScanType.InTransit, at: TestClock.Start.AddMinutes(25)),
            ], Make.CourierId);
        }

        await using var read = database.CreateContext();
        var page = await new EfShipmentStore(read).ListAsync(new ShipmentQuery(1, 50, ShipmentStatus.Created, city.ToUpperInvariant()));

        Assert.Equal(2, page.TotalCount);
        var views = page.Items.ToDictionary(view => view.TrackingNumber);
        Assert.Equal(ScanType.InTransit, views[withScans].LatestScan?.ScanType);
        Assert.Null(views[withoutScans].LatestScan);
    }

    [SqlServerFact]
    public async Task Deleting_a_shipment_deletes_its_scans()
    {
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var service = database.CreateService(db, _clock);
        await service.UploadScansAsync(trackingNumber, [Make.Scan()], Make.CourierId);

        Assert.True((await service.DeleteAsync(trackingNumber)).Succeeded);

        Assert.False(await db.Shipments.AnyAsync(item => item.TrackingNumber == trackingNumber));
        Assert.Equal(0, await db.Scans.CountAsync(scan => !db.Shipments.Any(shipment => shipment.Id == scan.ShipmentId)));
    }

    [SqlServerFact]
    public async Task The_database_itself_refuses_a_delivered_scan_without_a_signature()
    {
        var trackingNumber = await CreateShipmentAsync();
        await using var db = database.CreateContext();
        var shipment = await db.Shipments.SingleAsync(item => item.TrackingNumber == trackingNumber);
        db.Scans.Add(TrackingScan.Record(Guid.NewGuid(), shipment.Id, ScanType.Delivered, "CR-1", "Door", _clock.Now, null, null, _clock.Now));

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_Scans_DeliveredSignature", error.InnerException?.Message);
    }

    [SqlServerFact]
    public async Task The_api_runs_end_to_end_against_sql_server()
    {
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
        var trackingNumber = NextTrackingNumber();

        var created = await client.PostAsJsonAsync("/api/v2/shipments", ShipmentEndpointTests.NewShipment(trackingNumber));
        var batch = new { scans = new[] { new { scanId = Make.NewScanId(), scanType = "Pickup", courierId = "CR-1001", location = "Depot", scannedAtUtc = _clock.Now } } };
        var first = await client.PostAsJsonAsync($"/api/v2/shipments/{trackingNumber}/scans", batch);
        var again = await client.PostAsJsonAsync($"/api/v2/shipments/{trackingNumber}/scans", batch);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/v2/shipments/{trackingNumber}/scans");
        var duplicate = await client.PostAsJsonAsync("/api/v2/shipments", ShipmentEndpointTests.NewShipment(trackingNumber));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(1, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetInt32());
        Assert.Equal(1, (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("duplicatesSkipped").GetInt32());
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(2.5m, (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("weightKg").GetDecimal());
    }
}
