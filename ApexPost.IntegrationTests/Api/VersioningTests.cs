using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Tracking.Domain;
using ApexPost.Testing.Tracking;
using ApexPost.Testing.Api;

namespace ApexPost.IntegrationTests.Api;

public sealed class VersioningTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public VersioningTests()
    {
        _client = _factory.CreateClient();
        var shipment = _factory.Store.Seed(Make.ShipmentIn(ShipmentStatus.InTransit, "APX-10000001"));
        _factory.Store.SeedScan(TrackingScan.Record(Guid.NewGuid(), shipment.Id, ScanType.InTransit, "CR-1001", "Sydney hub", TestTime.Start.AddMinutes(20), null, null, TestTime.Start));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetShipmentV1_Known_ReturnsPartBShape()
    {
        // Act
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/shipments/APX-10000001");

        // Assert
        Assert.Equal("Mia Fictional", body.GetProperty("recipientName").GetString());
        Assert.Equal("Sydney", body.GetProperty("city").GetString());
        Assert.Equal((int)ShipmentStatus.InTransit, body.GetProperty("status").GetInt32());
        Assert.False(body.TryGetProperty("recipient", out _));
        Assert.False(body.TryGetProperty("latestScan", out _));
    }

    [Fact]
    public async Task GetShipmentV2_Known_ReturnsNestedRecipientLatestScanAndNamedStatus()
    {
        // Arrange
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000001");

        // Act
        var recipient = body.GetProperty("recipient");

        // Assert
        Assert.Equal("Mia Fictional", recipient.GetProperty("name").GetString());
        Assert.Equal("+61 2 5550 1234", recipient.GetProperty("phone").GetString());
        Assert.Equal("12 Example Lane", recipient.GetProperty("address").GetString());
        Assert.Equal("Sydney", recipient.GetProperty("city").GetString());
        Assert.Equal("InTransit", body.GetProperty("status").GetString());

        // Act
        var latest = body.GetProperty("latestScan");

        // Assert
        Assert.Equal("InTransit", latest.GetProperty("scanType").GetString());
        Assert.Equal("Sydney hub", latest.GetProperty("location").GetString());
        Assert.Equal(TestTime.Start.AddMinutes(20), latest.GetProperty("scannedAtUtc").GetDateTimeOffset());
        Assert.False(body.TryGetProperty("recipientName", out _));
    }

    [Fact]
    public async Task GetShipmentV2_NoScansYet_ReturnsNullLatestScan()
    {
        // Arrange
        _factory.Store.Seed(Make.Shipment("APX-10000002"));

        // Act
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000002");

        // Assert
        Assert.Equal(JsonValueKind.Null, body.GetProperty("latestScan").ValueKind);
    }

    [Fact]
    public async Task ListShipmentsV2_Any_UsesV2Shape()
    {
        // Act
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments?status=InTransit");

        // Assert
        Assert.Equal("InTransit", page.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task PostShipment_EachVersion_LocationPointsAtSameVersion(string version)
    {
        // Act
        var response = await _client.PostAsJsonAsync($"/api/{version}/shipments", ShipmentEndpointTests.NewShipment());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/{version}/shipments/APX-20000001", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task AnyResponse_VersionHeaders_ReportSupportedDeprecatedAndV1Sunset()
    {
        // Arrange
        var v1 = await _client.GetAsync("/api/v1/shipments/APX-10000001");

        // Act
        var v2 = await _client.GetAsync("/api/v2/shipments/APX-10000001");

        // Assert
        Assert.Equal("2.0", Assert.Single(v2.Headers.GetValues("api-supported-versions")));
        Assert.Equal("1.0", Assert.Single(v2.Headers.GetValues("api-deprecated-versions")));
        Assert.True(v1.Headers.Contains("Deprecation"));
        Assert.Equal("Wed, 30 Jun 2027 00:00:00 GMT", Assert.Single(v1.Headers.GetValues("Sunset")));
        Assert.Contains(v1.Headers.GetValues("Link"), link => link.Contains("rel=\"sunset\"", StringComparison.Ordinal));
        Assert.False(v2.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task UnversionedUrl_PartBClient_IsServedAsV1()
    {
        // Arrange
        var response = await _client.GetAsync("/api/shipments/APX-10000001?ignored=1");

        // Act
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/shipments?pageSize=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Number, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").ValueKind);
        Assert.Equal(1, list.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ScanEndpoints_V1AndV2_ShareTheSameService()
    {
        // Arrange
        var v1 = await _client.GetFromJsonAsync<JsonElement>("/api/v1/shipments/APX-10000001/scans");

        // Act
        var v2 = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000001/scans");

        // Assert
        Assert.Equal(v1.GetRawText(), v2.GetRawText());
    }

    [Fact]
    public async Task AnyEndpoint_UnknownVersion_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/v3/shipments/APX-10000001");

        // Assert
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
