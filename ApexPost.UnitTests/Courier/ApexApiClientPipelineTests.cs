using System.Net;
using System.Text.Json;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Testing.Courier;

namespace ApexPost.UnitTests.Courier;

public sealed class ApexApiClientPipelineTests : IDisposable
{
    private readonly ClientHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task The_auth_handler_sends_the_stored_token_as_a_bearer_header()
    {
        _harness.Server.Then(HttpStatusCode.OK, """{"trackingNumber":"APX-10000001","weightKg":1,"status":"Created","updatedAtUtc":"2026-11-09T01:00:00Z"}""");

        await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal($"Bearer {ClientHarness.Token}", _harness.Server.Received.Single().Authorization);
    }

    [Fact]
    public async Task A_401_clears_the_token_signs_out_is_not_retried_and_never_logs_the_token()
    {
        var signedOut = 0;
        _harness.Session.SignedOut += (_, _) => signedOut++;
        _harness.Server.Then(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401,"detail":"The access token has expired. Sign in again."}""", "application/problem+json");

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(ApiFailureKind.Unauthorized, result.Failure);
        Assert.Equal("The access token has expired. Sign in again.", result.Message);
        Assert.Null(_harness.Tokens.Token);
        Assert.Equal(1, signedOut);
        Assert.Equal(1, _harness.Server.Calls);
        Assert.DoesNotContain(ClientHarness.Token, _harness.Logs.All);
        Assert.DoesNotContain("eyJ", _harness.Logs.All);
    }

    [Fact]
    public async Task Without_a_token_no_header_is_sent_and_nobody_is_signed_out()
    {
        using var anonymous = new ClientHarness(token: null);
        var signedOut = false;
        anonymous.Session.SignedOut += (_, _) => signedOut = true;
        anonymous.Server.Then(HttpStatusCode.Unauthorized);

        await anonymous.Client.GetShipmentAsync("APX-10000001");

        Assert.Null(anonymous.Server.Received.Single().Authorization);
        Assert.False(signedOut);
    }

    [Fact]
    public async Task Unknown_and_null_fields_and_string_enums_are_read_without_trouble()
    {
        _harness.Server.Then(HttpStatusCode.OK, """
            {"trackingNumber":"APX-10000001","originBranchCode":"B001","recipient":null,"weightKg":2.5,"status":"OutForDelivery",
             "latestScan":{"scanType":"InTransit","location":null,"scannedAtUtc":"2026-11-09T01:00:00Z","futureField":1},
             "updatedAtUtc":"2026-11-09T02:00:00Z","addedInV3":{"nested":[1,2,3]}}
            """);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.True(result.Succeeded);
        Assert.Equal(ShipmentStatus.OutForDelivery, result.Value.Status);
        Assert.Null(result.Value.Recipient);
        Assert.Equal(ScanKind.InTransit, result.Value.LatestScan?.ScanType);
        Assert.Null(result.Value.LatestScan?.Location);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("{\"trackingNumber\":")]
    [InlineData("{\"trackingNumber\":\"APX-1\",\"status\":\"Teleported\",\"updatedAtUtc\":\"2026-11-09T02:00:00Z\"}")]
    [InlineData("null")]
    public async Task A_malformed_success_body_is_a_friendly_failure_not_a_crash(string body)
    {
        _harness.Server.Then(HttpStatusCode.OK, body);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(ApiFailureKind.Malformed, result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Validation_problem_details_become_a_readable_message()
    {
        _harness.Server.Then(HttpStatusCode.BadRequest, """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"scans[0].scannedAtUtc":["The ScannedAtUtc field cannot be in the future."],"scans[0].recipientSignatureName":["A Delivered scan needs the name of the person who signed for it."]}}
            """, "application/problem+json");

        var result = await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
        Assert.Equal("Please check: The ScannedAtUtc field cannot be in the future. A Delivered scan needs the name of the person who signed for it.", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, "The shipment was not found.")]
    [InlineData(HttpStatusCode.Forbidden, null, "You are not allowed to do that.")]
    [InlineData(HttpStatusCode.Conflict, """{"title":"Invalid status transition","status":409,"detail":"It can move to PickedUp."}""", "It can move to PickedUp.")]
    [InlineData(HttpStatusCode.Conflict, "not json at all", "The server refused the request (409).")]
    public async Task Problem_details_or_their_absence_give_a_friendly_message(HttpStatusCode status, string? body, string expected)
    {
        _harness.Server.Then(status, body);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(expected, result.Message);
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
    }

    [Fact]
    public async Task The_upload_is_camel_case_with_enum_names_and_no_null_fields()
    {
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0}""");

        await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        using var sent = JsonDocument.Parse(_harness.Server.Received.Single().Body!);
        var scan = sent.RootElement.GetProperty("scans")[0];
        Assert.Equal("Pickup", scan.GetProperty("scanType").GetString());
        Assert.StartsWith("scan::", scan.GetProperty("scanId").GetString());
        Assert.False(scan.TryGetProperty("recipientSignatureName", out _));
        Assert.Equal("/api/v2/shipments/APX-10000001/scans", _harness.Server.Received.Single().Path);
    }

    [Fact]
    public void The_courier_id_is_read_from_the_token_and_garbage_gives_null()
    {
        Assert.Equal("CR-1001", CourierIdentity.CourierIdFrom(ClientHarness.Token));
        Assert.Null(CourierIdentity.CourierIdFrom("not.a.token!"));
        Assert.Null(CourierIdentity.CourierIdFrom(null));
        Assert.Null(CourierIdentity.CourierIdFrom("a.eyJzdWIiOiJ4In0.c"));
    }

    [Fact]
    public void The_json_options_come_from_the_source_generated_context()
    {
        Assert.Same(ApexJsonContext.Default.Options, ApexJson.Options);
        Assert.NotNull(ApexJson.Options.TypeInfoResolver);
        Assert.True(ApexJson.Options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.CamelCase, ApexJson.Options.PropertyNamingPolicy);
    }
}
