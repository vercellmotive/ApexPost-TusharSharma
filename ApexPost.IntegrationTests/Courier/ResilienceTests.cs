using System.Net;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Testing.Courier;

namespace ApexPost.IntegrationTests.Courier;

public sealed class ResilienceTests : IDisposable
{
    private const string Shipment = """{"trackingNumber":"APX-10000001","weightKg":2.5,"status":"InTransit","updatedAtUtc":"2026-11-09T01:00:00Z"}""";
    private readonly ClientHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetShipment_ServerErrorThenSuccess_RetriesAndReturnsSuccess()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK, Shipment);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(3, _harness.Server.Calls);
        Assert.Contains("Retry 1 of 3 for GET /api/v2/shipments/APX-10000001", _harness.Logs.All);
        Assert.Contains("after HTTP 503", _harness.Logs.All);
        Assert.Contains("Retry 2 of 3", _harness.Logs.All);
    }

    [Fact]
    public async Task GetShipment_Always5xx_StopsAfterThreeRetries()
    {
        // Arrange
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.InternalServerError, null));
        using var lenient = new ClientHarness(options: ClientHarness.FastOptions with { MinimumThroughput = 100 });
        lenient.Server.Fallback = _harness.Server.Fallback;

        // Act
        var result = await lenient.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.ServerError, result.Failure);
        Assert.True(result.IsTransient);
        Assert.Equal(4, lenient.Server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task GetShipment_TimeoutOrServerError_IsRetried(HttpStatusCode status)
    {
        // Arrange
        _harness.Server.Then(status).Then(HttpStatusCode.OK, Shipment);

        // Act and assert
        Assert.True((await _harness.Client.GetShipmentAsync("APX-10000001")).Succeeded);
        Assert.Equal(2, _harness.Server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetShipment_ClientError_IsNeverRetried(HttpStatusCode status)
    {
        // Arrange
        _harness.Server.Then(status).Then(HttpStatusCode.OK, Shipment);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(1, _harness.Server.Calls);
        Assert.DoesNotContain("Retry 1 of", _harness.Logs.All);
    }

    [Fact]
    public async Task GetShipment_NetworkError_IsRetried()
    {
        // Arrange
        _harness.Server.ThenThrow(new HttpRequestException("Connection refused")).Then(HttpStatusCode.OK, Shipment);

        // Act and assert
        Assert.True((await _harness.Client.GetShipmentAsync("APX-10000001")).Succeeded);
        Assert.Contains("after HttpRequestException", _harness.Logs.All);
    }

    [Fact]
    public async Task GetShipment_SlowAttempt_TimesOutAndRetries()
    {
        // Arrange
        _harness.Server.ThenSlow(TimeSpan.FromSeconds(5), HttpStatusCode.OK, Shipment).Then(HttpStatusCode.OK, Shipment);

        // Act
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Contains("after TimeoutRejectedException", _harness.Logs.All);
    }

    [Fact]
    public async Task GetShipment_OverallTimeout_ReturnsUnreachable()
    {
        // Arrange
        using var harness = new ClientHarness(options: ClientHarness.FastOptions with { TotalTimeout = TimeSpan.FromMilliseconds(500), MinimumThroughput = 100 });
        harness.Server.Fallback = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), harness.Server.RequestAborted);
            return ScriptedServer.Respond(HttpStatusCode.OK, Shipment);
        };

        // Act
        var result = await harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Unreachable, result.Failure);
        Assert.Contains("overall timeout", harness.Logs.All);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    [InlineData("HEAD", true)]
    [InlineData("POST", false)]
    [InlineData("PATCH", false)]
    public void Pipeline_HttpMethod_RetriesOnlyIdempotentRequests(string method, bool expected)
    {
        // Arrange
        using var request = new HttpRequestMessage(new HttpMethod(method), "https://api.test/x");

        // Act
        var retryable = ApexResilience.IsIdempotent(request);

        // Assert
        Assert.Equal(expected, retryable);
    }

    [Fact]
    public void Pipeline_PostMarkedIdempotent_IsRetried()
    {
        // Arrange
        var upload = new HttpRequestMessage(HttpMethod.Post, "https://api.test/x");

        // Act
        upload.Options.Set(ApexApiClient.Idempotent, true);

        // Assert
        Assert.True(ApexResilience.IsIdempotent(upload));
    }

    [Fact]
    public async Task UploadScans_ServerError_IsRetriedBecauseScanIdsAreIdempotent()
    {
        // Arrange
        _harness.Server.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");

        // Act
        var result = await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(2, _harness.Server.Calls);

        // Act
        var bodies = _harness.Server.Received.Select(request => request.Body).ToList();

        // Assert
        Assert.Equal(bodies[0], bodies[1]);
    }

    [Fact]
    public async Task CircuitBreaker_RepeatedFailures_OpensAndShortCircuits()
    {
        // Arrange
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));
        var changes = new List<CircuitState>();
        _harness.Circuit.Changed += (_, state) => changes.Add(state);

        var first = await _harness.Client.GetShipmentAsync("APX-10000001");
        var callsWhenOpen = _harness.Server.Calls;

        // Act
        var second = await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.ServerError, first.Failure);
        Assert.Equal(ApiFailureKind.CircuitOpen, second.Failure);
        Assert.Equal(ApexApiClient.OfflineMessage, second.Message);
        Assert.Equal(callsWhenOpen, _harness.Server.Calls);
        Assert.Equal(CircuitState.Open, _harness.Circuit.State);
        Assert.Equal([CircuitState.Open], changes);
        Assert.Contains("Circuit opened", _harness.Logs.All);
    }

    [Fact]
    public async Task CircuitBreaker_SuccessAfterBreak_ClosesAgain()
    {
        // Arrange
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));

        // Act
        await _harness.Client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.True(_harness.Circuit.IsOpen);

        // Arrange (next step)
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.OK, Shipment));
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        // While the circuit is open every call is refused at once, without reaching the server. Calling until one gets
        // through waits exactly as long as the break lasts, instead of sleeping for a guessed time.
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");
        while (result.Failure == ApiFailureKind.CircuitOpen && !bound.IsCancellationRequested)
        {
            await Task.Delay(20, CancellationToken.None);
            result = await _harness.Client.GetShipmentAsync("APX-10000001");
        }

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(CircuitState.Closed, _harness.Circuit.State);
        Assert.Contains("Circuit half-open", _harness.Logs.All);
        Assert.Contains("Circuit closed", _harness.Logs.All);
    }

    [Fact]
    public async Task DefaultOptions_Values_MatchResilienceMd()
    {
        // Act
        var defaults = new ApexResilienceOptions();

        // Assert
        Assert.Equal(3, defaults.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), defaults.RetryBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), defaults.AttemptTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), defaults.TotalTimeout);
        Assert.Equal(0.5, defaults.FailureRatio);
        Assert.Equal(4, defaults.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.BreakDuration);
        await Task.CompletedTask;
    }
}
