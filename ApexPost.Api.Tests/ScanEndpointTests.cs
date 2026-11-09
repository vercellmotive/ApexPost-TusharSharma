using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Tracking.Domain;
using static ApexPost.Api.Tests.ShipmentEndpointTests;

namespace ApexPost.Api.Tests;

public sealed class ScanEndpointTests : IDisposable
{
    private const string Scans = "/api/v2/shipments/APX-10000001/scans";
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ScanEndpointTests()
    {
        _client = _factory.CreateClient();
        _factory.Store.Seed(Make.Shipment("APX-10000001"));
    }

    public void Dispose() => _factory.Dispose();

    private static Dictionary<string, object?> Scan(string? scanId = null, string type = "Pickup", DateTimeOffset? at = null, string? signature = null) => new()
    {
        ["scanId"] = scanId ?? Make.NewScanId(),
        ["scanType"] = type,
        ["courierId"] = "CR-1001",
        ["location"] = "Sydney depot",
        ["scannedAtUtc"] = at ?? TestClock.Start.AddMinutes(30),
        ["notes"] = "Fictional",
        ["recipientSignatureName"] = signature,
    };

    [Fact]
    public async Task Re_uploading_the_same_scans_skips_them_as_duplicates()
    {
        var batch = new { scans = new[] { Scan(), Scan(type: "InTransit") } };

        var first = await _client.PostAsJsonAsync(Scans, batch);
        var second = await _client.PostAsJsonAsync(Scans, batch);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((2, 0, 0), Counts(await first.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal((0, 2, 0), Counts(await second.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(2, _factory.Store.AllScans.Count);
    }

    [Fact]
    public async Task Rejected_scans_are_counted_and_explained()
    {
        var early = Scan(at: TestClock.Start.AddHours(-2));
        var response = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(), early } });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((1, 0, 1), Counts(body));
        var rejection = body.GetProperty("rejections")[0];
        Assert.Equal(early["scanId"], rejection.GetProperty("scanId").GetString());
        Assert.Contains("before the shipment was created", rejection.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_scan_from_the_future_fails_validation_with_the_custom_attribute()
    {
        var errors = await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(at: _factory.Clock.Now.AddHours(1)) } }));

        Assert.Contains("cannot be in the future", Assert.Single(errors["scans[0].scannedAtUtc"]));
    }

    [Fact]
    public async Task A_delivered_scan_without_a_signature_fails_the_custom_validatable_rule()
    {
        var unsigned = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(type: "Delivered") } });
        var signed = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(type: "Delivered", signature: "Mia Fictional") } });

        var errors = await ReadValidationErrors(unsigned);
        Assert.Contains("signed for it", Assert.Single(errors["scans[0].recipientSignatureName"]));
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
    }

    [Theory]
    [InlineData("scanId", "3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    [InlineData("scanId", "scan::3F2504E0-4F89-41D3-9A0C-0305E82C3301")]
    [InlineData("scanType", "Lost")]
    [InlineData("courierId", "CR 1001")]
    [InlineData("location", "")]
    public async Task Bad_scan_fields_return_400(string field, string value)
    {
        var scan = Scan();
        scan[field] = value;

        var response = await _client.PostAsJsonAsync(Scans, new { scans = new[] { scan } });

        await ReadProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_or_oversized_batch_returns_400()
    {
        var tooMany = Enumerable.Range(0, 101).Select(_ => Scan()).ToArray();

        await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = Array.Empty<object>() }));
        await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = tooMany }));
    }

    [Fact]
    public async Task Uploading_to_an_unknown_shipment_returns_404()
    {
        var response = await _client.PostAsJsonAsync("/api/v2/shipments/APX-99999999/scans", new { scans = new[] { Scan() } });

        await ReadProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Scan_history_comes_back_oldest_first_with_part_a_ids()
    {
        var later = Scan(type: "InTransit", at: TestClock.Start.AddMinutes(50));
        var earlier = Scan(at: TestClock.Start.AddMinutes(10));
        await _client.PostAsJsonAsync(Scans, new { scans = new[] { later, earlier } });

        var history = await _client.GetFromJsonAsync<JsonElement>(Scans);

        Assert.Equal([earlier["scanId"], later["scanId"]], history.EnumerateArray().Select(scan => (object?)scan.GetProperty("scanId").GetString()));
        Assert.Equal("Pickup", history[0].GetProperty("scanType").GetString());
        await ReadProblem(await _client.GetAsync("/api/v2/shipments/APX-99999999/scans"), HttpStatusCode.NotFound);
    }

    private static (int Accepted, int Duplicates, int Rejected) Counts(JsonElement body) =>
        (body.GetProperty("accepted").GetInt32(), body.GetProperty("duplicatesSkipped").GetInt32(), body.GetProperty("rejected").GetInt32());
}
