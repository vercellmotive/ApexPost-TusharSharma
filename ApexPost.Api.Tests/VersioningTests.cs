using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Tests;

public sealed class VersioningTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public VersioningTests()
    {
        _client = _factory.CreateClient();
        var shipment = _factory.Store.Seed(Make.ShipmentIn(ShipmentStatus.InTransit, "APX-10000001"));
        _factory.Store.SeedScan(TrackingScan.Record(Guid.NewGuid(), shipment.Id, ScanType.InTransit, "CR-1001", "Sydney hub", TestClock.Start.AddMinutes(20), null, null, TestClock.Start));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task V1_keeps_the_part_b_shape_with_flat_recipient_fields_and_a_numeric_status()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v1/shipments/APX-10000001");

        Assert.Equal("Mia Fictional", body.GetProperty("recipientName").GetString());
        Assert.Equal("Sydney", body.GetProperty("city").GetString());
        Assert.Equal((int)ShipmentStatus.InTransit, body.GetProperty("status").GetInt32());
        Assert.False(body.TryGetProperty("recipient", out _));
        Assert.False(body.TryGetProperty("latestScan", out _));
    }

    [Fact]
    public async Task V2_nests_the_recipient_adds_the_latest_scan_and_names_the_status()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000001");

        var recipient = body.GetProperty("recipient");
        Assert.Equal("Mia Fictional", recipient.GetProperty("name").GetString());
        Assert.Equal("+61 2 5550 1234", recipient.GetProperty("phone").GetString());
        Assert.Equal("12 Example Lane", recipient.GetProperty("address").GetString());
        Assert.Equal("Sydney", recipient.GetProperty("city").GetString());
        Assert.Equal("InTransit", body.GetProperty("status").GetString());
        var latest = body.GetProperty("latestScan");
        Assert.Equal("InTransit", latest.GetProperty("scanType").GetString());
        Assert.Equal("Sydney hub", latest.GetProperty("location").GetString());
        Assert.Equal(TestClock.Start.AddMinutes(20), latest.GetProperty("scannedAtUtc").GetDateTimeOffset());
        Assert.False(body.TryGetProperty("recipientName", out _));
    }

    [Fact]
    public async Task V2_returns_null_for_the_latest_scan_before_the_first_scan()
    {
        _factory.Store.Seed(Make.Shipment("APX-10000002"));

        var body = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000002");

        Assert.Equal(JsonValueKind.Null, body.GetProperty("latestScan").ValueKind);
    }

    [Fact]
    public async Task V2_list_uses_the_v2_shape()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments?status=InTransit");

        Assert.Equal("InTransit", page.GetProperty("items")[0].GetProperty("status").GetString());
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task Create_in_each_version_points_the_location_header_at_the_same_version(string version)
    {
        var response = await _client.PostAsJsonAsync($"/api/{version}/shipments", ShipmentEndpointTests.NewShipment());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/{version}/shipments/APX-20000001", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Responses_report_supported_and_deprecated_versions_and_v1_announces_its_sunset()
    {
        var v1 = await _client.GetAsync("/api/v1/shipments/APX-10000001");
        var v2 = await _client.GetAsync("/api/v2/shipments/APX-10000001");

        Assert.Equal("2.0", Assert.Single(v2.Headers.GetValues("api-supported-versions")));
        Assert.Equal("1.0", Assert.Single(v2.Headers.GetValues("api-deprecated-versions")));
        Assert.True(v1.Headers.Contains("Deprecation"));
        Assert.Equal("Wed, 30 Jun 2027 00:00:00 GMT", Assert.Single(v1.Headers.GetValues("Sunset")));
        Assert.Contains(v1.Headers.GetValues("Link"), link => link.Contains("rel=\"sunset\"", StringComparison.Ordinal));
        Assert.False(v2.Headers.Contains("Sunset"));
    }

    [Fact]
    public async Task Part_b_urls_without_a_version_still_work_as_v1()
    {
        var response = await _client.GetAsync("/api/shipments/APX-10000001?ignored=1");
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/shipments?pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Number, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").ValueKind);
        Assert.Equal(1, list.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Both_versions_share_the_scan_endpoints()
    {
        var v1 = await _client.GetFromJsonAsync<JsonElement>("/api/v1/shipments/APX-10000001/scans");
        var v2 = await _client.GetFromJsonAsync<JsonElement>("/api/v2/shipments/APX-10000001/scans");

        Assert.Equal(v1.GetRawText(), v2.GetRawText());
    }

    [Fact]
    public async Task An_unknown_version_is_not_found()
    {
        var response = await _client.GetAsync("/api/v3/shipments/APX-10000001");

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
