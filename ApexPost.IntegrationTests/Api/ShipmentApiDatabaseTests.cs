using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Testing.Api;
using ApexPost.Testing.Tracking;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.IntegrationTests.Api;

/// <summary>
/// R2, R3 and R4 end to end: HTTP request in, row in a real database out. Each test class instance gets its own API
/// and its own SQLite database, so the tests share nothing. The other API tests use the in-memory store for speed;
/// these prove that the same requests also work through EF Core and SQL.
/// </summary>
public sealed class ShipmentApiDatabaseTests : IDisposable
{
    private const string Shipments = "/api/v2/shipments";
    private readonly SqliteApiFactory _api = new();

    public void Dispose() => _api.Dispose();

    private static object NewShipment(string trackingNumber = "APX-30000001") => new
    {
        trackingNumber,
        originBranchCode = "B001",
        recipientName = "Mia Fictional",
        recipientPhone = "+61 2 5550 1234",
        recipientAddress = "12 Example Lane",
        city = "Sydney",
        weightKg = 2.5m,
    };

    [Fact]
    public async Task PostShipment_AsAdmin_StoresTheRowAndTheLocationReadsItBack()
    {
        // Arrange
        var admin = _api.CreateClient(ApexPostRoles.Admin);

        // Act
        var created = await admin.PostAsJsonAsync(Shipments, NewShipment());
        var readBack = await admin.GetAsync(created.Headers.Location);

        // Assert
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        var body = await readBack.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", body.GetProperty("status").GetString());
        Assert.Equal("Sydney", body.GetProperty("recipient").GetProperty("city").GetString());
        await using var db = _api.Database.CreateContext();
        var row = await db.Shipments.SingleAsync();
        Assert.Equal("APX-30000001", row.TrackingNumber);
        Assert.Equal(TestTime.NewClock().GetUtcNow(), row.CreatedAtUtc);
    }

    [Fact]
    public async Task PostShipment_DuplicateTrackingNumber_Returns409AndKeepsOneRow()
    {
        // Arrange
        var admin = _api.CreateClient(ApexPostRoles.Admin);
        await admin.PostAsJsonAsync(Shipments, NewShipment());

        // Act
        var duplicate = await admin.PostAsJsonAsync(Shipments, NewShipment());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);
        await using var db = _api.Database.CreateContext();
        Assert.Equal(1, await db.Shipments.CountAsync());
    }

    [Fact]
    public async Task PostScans_SameBatchTwiceAsTheCourier_StoresEachScanOnce()
    {
        // Arrange
        await _api.CreateClient(ApexPostRoles.Admin).PostAsJsonAsync(Shipments, NewShipment());
        var created = _api.Clock.GetUtcNow();
        _api.Clock.Advance(TimeSpan.FromMinutes(30));
        var courier = _api.CreateClient(ApexPostRoles.Courier);
        var batch = new
        {
            scans = new[]
            {
                new { scanId = Make.NewScanId(), scanType = "Pickup", courierId = Make.CourierId, location = "Sydney depot", scannedAtUtc = created.AddMinutes(10) },
                new { scanId = Make.NewScanId(), scanType = "InTransit", courierId = Make.CourierId, location = "Parramatta hub", scannedAtUtc = created.AddMinutes(20) },
            },
        };

        // Act
        var first = await courier.PostAsJsonAsync($"{Shipments}/APX-30000001/scans", batch);
        var second = await courier.PostAsJsonAsync($"{Shipments}/APX-30000001/scans", batch);

        // Assert
        Assert.Equal(2, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accepted").GetInt32());
        var repeat = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, repeat.GetProperty("accepted").GetInt32());
        Assert.Equal(2, repeat.GetProperty("duplicatesSkipped").GetInt32());
        await using var db = _api.Database.CreateContext();
        Assert.Equal(2, await db.Scans.CountAsync());
    }

    [Fact]
    public async Task DeleteShipment_AsCourier_Returns403AndTheRowStays()
    {
        // Arrange
        await _api.CreateClient(ApexPostRoles.Admin).PostAsJsonAsync(Shipments, NewShipment());
        var courier = _api.CreateClient(ApexPostRoles.Courier);

        // Act
        var response = await courier.DeleteAsync($"{Shipments}/APX-30000001");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = _api.Database.CreateContext();
        Assert.Equal(1, await db.Shipments.CountAsync());
    }

    [Fact]
    public async Task DeleteShipment_AsAdminWhileCreated_Returns204AndRemovesTheRow()
    {
        // Arrange
        var admin = _api.CreateClient(ApexPostRoles.Admin);
        await admin.PostAsJsonAsync(Shipments, NewShipment());

        // Act
        var response = await admin.DeleteAsync($"{Shipments}/APX-30000001");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = _api.Database.CreateContext();
        Assert.Equal(0, await db.Shipments.CountAsync());
    }
}
