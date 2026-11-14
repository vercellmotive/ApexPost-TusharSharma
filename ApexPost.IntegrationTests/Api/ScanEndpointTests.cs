using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Tracking.Domain;
using static ApexPost.IntegrationTests.Api.ShipmentEndpointTests;
using ApexPost.Testing.Tracking;
using ApexPost.Testing.Api;

namespace ApexPost.IntegrationTests.Api;

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
        ["scannedAtUtc"] = at ?? TestTime.Start.AddMinutes(30),
        ["notes"] = "Fictional",
        ["recipientSignatureName"] = signature,
    };

    [Fact]
    public async Task PostScans_SameBatchTwice_SkipsRepeatsAsDuplicates()
    {
        // Arrange
        var batch = new { scans = new[] { Scan(), Scan(type: "InTransit") } };

        var first = await _client.PostAsJsonAsync(Scans, batch);

        // Act
        var second = await _client.PostAsJsonAsync(Scans, batch);

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((2, 0, 0), Counts(await first.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal((0, 2, 0), Counts(await second.Content.ReadFromJsonAsync<JsonElement>()));
        Assert.Equal(2, _factory.Store.AllScans.Count);
    }

    [Fact]
    public async Task PostScans_ScansBreakingServerRules_CountsAndExplainsRejections()
    {
        // Arrange
        var early = Scan(at: TestTime.Start.AddHours(-2));

        // Act
        var response = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(), early } });

        // Assert
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal((1, 0, 1), Counts(body));

        // Act
        var rejection = body.GetProperty("rejections")[0];

        // Assert
        Assert.Equal(early["scanId"], rejection.GetProperty("scanId").GetString());
        Assert.Contains("before the shipment was created", rejection.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task PostScans_ScanTimeInFuture_Returns400FromCustomAttribute()
    {
        // Act
        var errors = await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(at: _factory.Clock.GetUtcNow().AddHours(1)) } }));

        // Assert
        Assert.Contains("cannot be in the future", Assert.Single(errors["scans[0].scannedAtUtc"]));
    }

    [Fact]
    public async Task PostScans_DeliveredWithoutSignature_Returns400FromValidatableRule()
    {
        // Arrange
        var unsigned = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(type: "Delivered") } });
        var signed = await _client.PostAsJsonAsync(Scans, new { scans = new[] { Scan(type: "Delivered", signature: "Mia Fictional") } });

        // Act
        var errors = await ReadValidationErrors(unsigned);

        // Assert
        Assert.Contains("signed for it", Assert.Single(errors["scans[0].recipientSignatureName"]));
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
    }

    [Theory]
    [InlineData("scanId", "3f2504e0-4f89-41d3-9a0c-0305e82c3301")]
    [InlineData("scanId", "scan::3F2504E0-4F89-41D3-9A0C-0305E82C3301")]
    [InlineData("scanType", "Lost")]
    [InlineData("courierId", "CR 1001")]
    [InlineData("location", "")]
    public async Task PostScans_InvalidFields_Returns400(string field, string value)
    {
        // Arrange
        var scan = Scan();
        scan[field] = value;

        // Act
        var response = await _client.PostAsJsonAsync(Scans, new { scans = new[] { scan } });

        // Assert
        await ReadProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostScans_EmptyOrOversizedBatch_Returns400()
    {
        // Arrange
        var tooMany = Enumerable.Range(0, 101).Select(_ => Scan()).ToArray();

        // Act and assert
        await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = Array.Empty<object>() }));

        await ReadValidationErrors(await _client.PostAsJsonAsync(Scans, new { scans = tooMany }));
    }

    [Fact]
    public async Task PostScans_UnknownShipment_Returns404()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/v2/shipments/APX-99999999/scans", new { scans = new[] { Scan() } });

        // Assert
        await ReadProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetScans_KnownShipment_ReturnsOldestFirstWithPartAIds()
    {
        // Arrange
        var later = Scan(type: "InTransit", at: TestTime.Start.AddMinutes(50));
        var earlier = Scan(at: TestTime.Start.AddMinutes(10));
        await _client.PostAsJsonAsync(Scans, new { scans = new[] { later, earlier } });

        // Act
        var history = await _client.GetFromJsonAsync<JsonElement>(Scans);

        // Assert
        Assert.Equal([earlier["scanId"], later["scanId"]], history.EnumerateArray().Select(scan => (object?)scan.GetProperty("scanId").GetString()));
        Assert.Equal("Pickup", history[0].GetProperty("scanType").GetString());
        await ReadProblem(await _client.GetAsync("/api/v2/shipments/APX-99999999/scans"), HttpStatusCode.NotFound);
    }

    private static (int Accepted, int Duplicates, int Rejected) Counts(JsonElement body) =>
        (body.GetProperty("accepted").GetInt32(), body.GetProperty("duplicatesSkipped").GetInt32(), body.GetProperty("rejected").GetInt32());
}
