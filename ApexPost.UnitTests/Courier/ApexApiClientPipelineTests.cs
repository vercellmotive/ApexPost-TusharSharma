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
    public async Task AuthHeaderHandler_StoredToken_SendsBearerHeader()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.OK, """{"trackingNumber":"APX-10000001","weightKg":1,"status":"Created","updatedAtUtc":"2026-11-09T01:00:00Z"}""");

        // Act
        await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal($"Bearer {ClientHarness.Token}", _harness.Server.Received.Single().Authorization);
    }

    [Fact]
    public async Task GetShipment_Unauthorized_ClearsTokenSignsOutAndNeverLogsToken()
    {
        // Arrange
        var signedOut = 0;
        _harness.Session.SignedOut += (_, _) => signedOut++;
        _harness.Server.Then(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401,"detail":"The access token has expired. Sign in again."}""", "application/problem+json");

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Unauthorized, result.Failure);
        Assert.Equal("The access token has expired. Sign in again.", result.Message);
        Assert.Null(_harness.Tokens.Token);
        Assert.Equal(1, signedOut);
        Assert.Equal(1, _harness.Server.Calls);
        Assert.DoesNotContain(ClientHarness.Token, _harness.Logs.All);
        Assert.DoesNotContain("eyJ", _harness.Logs.All);
    }

    [Fact]
    public async Task AuthHeaderHandler_NoToken_SendsNoHeaderAndDoesNotSignOut()
    {
        // Arrange
        using var anonymous = new ClientHarness(token: null);
        var signedOut = false;
        anonymous.Session.SignedOut += (_, _) => signedOut = true;
        anonymous.Server.Then(HttpStatusCode.Unauthorized);

        // Act
        await anonymous.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Null(anonymous.Server.Received.Single().Authorization);
        Assert.False(signedOut);
    }

    [Fact]
    public async Task GetShipment_UnknownAndNullFields_ReadsWithoutError()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.OK, """
            {"trackingNumber":"APX-10000001","originBranchCode":"B001","recipient":null,"weightKg":2.5,"status":"OutForDelivery",
             "latestScan":{"scanType":"InTransit","location":null,"scannedAtUtc":"2026-11-09T01:00:00Z","futureField":1},
             "updatedAtUtc":"2026-11-09T02:00:00Z","addedInV3":{"nested":[1,2,3]}}
            """);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
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
    public async Task GetShipment_MalformedSuccessBody_ReturnsMalformedFailure(string body)
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.OK, body);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Malformed, result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task UploadScans_ValidationProblemDetails_ReturnsReadableMessage()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.BadRequest, """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"scans[0].scannedAtUtc":["The ScannedAtUtc field cannot be in the future."],"scans[0].recipientSignatureName":["A Delivered scan needs the name of the person who signed for it."]}}
            """, "application/problem+json");

        // Act
        var result = await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        // Assert
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
        Assert.Equal("Please check: The ScannedAtUtc field cannot be in the future. A Delivered scan needs the name of the person who signed for it.", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, "The shipment was not found.")]
    [InlineData(HttpStatusCode.Forbidden, null, "You are not allowed to do that.")]
    [InlineData(HttpStatusCode.Conflict, """{"title":"Invalid status transition","status":409,"detail":"It can move to PickedUp."}""", "It can move to PickedUp.")]
    [InlineData(HttpStatusCode.Conflict, "not json at all", "The server refused the request (409).")]
    public async Task FriendlyMessage_ProblemDetailsOrNone_ReturnsFriendlyText(HttpStatusCode status, string? body, string expected)
    {
        // Arrange
        _harness.Server.Then(status, body);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(expected, result.Message);
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
    }

    [Fact]
    public async Task UploadScans_RequestBody_IsCamelCaseWithEnumNamesAndNoNulls()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0}""");

        // Act
        await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        // Assert
        using var sent = JsonDocument.Parse(_harness.Server.Received.Single().Body!);

        var scan = sent.RootElement.GetProperty("scans")[0];

        Assert.Equal("Pickup", scan.GetProperty("scanType").GetString());
        Assert.StartsWith("scan::", scan.GetProperty("scanId").GetString());
        Assert.False(scan.TryGetProperty("recipientSignatureName", out _));
        Assert.Equal("/api/v2/shipments/APX-10000001/scans", _harness.Server.Received.Single().Path);
    }

    [Fact]
    public void CourierIdentity_TokenOrGarbage_ReadsCourierIdOrNull()
    {
        // Act and assert
        Assert.Equal("CR-1001", CourierIdentity.CourierIdFrom(ClientHarness.Token));
        Assert.Null(CourierIdentity.CourierIdFrom("not.a.token!"));
        Assert.Null(CourierIdentity.CourierIdFrom(null));
        Assert.Null(CourierIdentity.CourierIdFrom("a.eyJzdWIiOiJ4In0.c"));
    }

    [Fact]
    public void ApexJson_Options_ComeFromSourceGeneratedContext()
    {
        // Act and assert
        Assert.Same(ApexJsonContext.Default.Options, ApexJson.Options);
        Assert.NotNull(ApexJson.Options.TypeInfoResolver);
        Assert.True(ApexJson.Options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.CamelCase, ApexJson.Options.PropertyNamingPolicy);
    }
}
