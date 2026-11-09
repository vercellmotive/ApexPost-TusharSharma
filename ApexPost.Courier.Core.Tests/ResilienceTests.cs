using System.Net;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Resilience;

namespace ApexPost.Courier.Core.Tests;

public sealed class ResilienceTests : IDisposable
{
    private const string Shipment = """{"trackingNumber":"APX-10000001","weightKg":2.5,"status":"InTransit","updatedAtUtc":"2026-11-09T01:00:00Z"}""";
    private readonly ClientHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_5xx_is_retried_and_a_later_success_is_returned()
    {
        _harness.Server.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.BadGateway).Then(HttpStatusCode.OK, Shipment);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.True(result.Succeeded);
        Assert.Equal(3, _harness.Server.Calls);
        Assert.Contains("Retry 1 of 3 for GET /api/v2/shipments/APX-10000001", _harness.Logs.All);
        Assert.Contains("after HTTP 503", _harness.Logs.All);
        Assert.Contains("Retry 2 of 3", _harness.Logs.All);
    }

    [Fact]
    public async Task Three_retries_at_most_then_a_server_error_result()
    {
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.InternalServerError, null));
        using var lenient = new ClientHarness(options: ClientHarness.FastOptions with { MinimumThroughput = 100 });
        lenient.Server.Fallback = _harness.Server.Fallback;

        var result = await lenient.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(ApiFailureKind.ServerError, result.Failure);
        Assert.True(result.IsTransient);
        Assert.Equal(4, lenient.Server.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Status_408_and_5xx_are_retried(HttpStatusCode status)
    {
        _harness.Server.Then(status).Then(HttpStatusCode.OK, Shipment);

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
    public async Task A_4xx_is_never_retried(HttpStatusCode status)
    {
        _harness.Server.Then(status).Then(HttpStatusCode.OK, Shipment);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.False(result.Succeeded);
        Assert.Equal(1, _harness.Server.Calls);
        Assert.DoesNotContain("Retry 1 of", _harness.Logs.All);
    }

    [Fact]
    public async Task A_network_error_is_retried()
    {
        _harness.Server.ThenThrow(new HttpRequestException("Connection refused")).Then(HttpStatusCode.OK, Shipment);

        Assert.True((await _harness.Client.GetShipmentAsync("APX-10000001")).Succeeded);
        Assert.Contains("after HttpRequestException", _harness.Logs.All);
    }

    [Fact]
    public async Task A_slow_attempt_times_out_and_is_retried()
    {
        _harness.Server.ThenSlow(TimeSpan.FromSeconds(5), HttpStatusCode.OK, Shipment).Then(HttpStatusCode.OK, Shipment);

        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.True(result.Succeeded);
        Assert.Contains("after TimeoutRejectedException", _harness.Logs.All);
    }

    [Fact]
    public async Task The_overall_timeout_ends_the_request_as_unreachable()
    {
        using var harness = new ClientHarness(options: ClientHarness.FastOptions with { TotalTimeout = TimeSpan.FromMilliseconds(500), MinimumThroughput = 100 });
        harness.Server.Fallback = async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), harness.Server.RequestAborted);
            return ScriptedServer.Respond(HttpStatusCode.OK, Shipment);
        };

        var result = await harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(ApiFailureKind.Unreachable, result.Failure);
        Assert.Contains("overall timeout", harness.Logs.All);
    }

    [Fact]
    public async Task A_post_that_is_not_idempotent_is_not_retried()
    {
        _harness.Server.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.OK, """{"accessToken":"x","expiresAtUtc":"2026-11-09T01:00:00Z"}""");

        var result = await _harness.Client.RequestDevelopmentTokenAsync("CR-1001");

        Assert.Equal(ApiFailureKind.ServerError, result.Failure);
        Assert.Equal(1, _harness.Server.Calls);
    }

    [Fact]
    public async Task The_scan_upload_post_is_retried_because_scan_ids_make_it_idempotent()
    {
        _harness.Server.Then(HttpStatusCode.ServiceUnavailable).Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");

        var result = await _harness.Client.UploadScansAsync("APX-10000001", [Scans.Item()]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, _harness.Server.Calls);
        var bodies = _harness.Server.Received.Select(request => request.Body).ToList();
        Assert.Equal(bodies[0], bodies[1]);
    }

    [Fact]
    public async Task The_circuit_opens_after_repeated_failures_and_short_circuits_calls()
    {
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));
        var changes = new List<CircuitState>();
        _harness.Circuit.Changed += (_, state) => changes.Add(state);

        var first = await _harness.Client.GetShipmentAsync("APX-10000001");
        var callsWhenOpen = _harness.Server.Calls;
        var second = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.Equal(ApiFailureKind.ServerError, first.Failure);
        Assert.Equal(ApiFailureKind.CircuitOpen, second.Failure);
        Assert.Equal(ApexApiClient.OfflineMessage, second.Message);
        Assert.Equal(callsWhenOpen, _harness.Server.Calls);
        Assert.Equal(CircuitState.Open, _harness.Circuit.State);
        Assert.Equal([CircuitState.Open], changes);
        Assert.Contains("Circuit opened", _harness.Logs.All);
    }

    [Fact]
    public async Task After_the_break_one_success_closes_the_circuit_again()
    {
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));
        await _harness.Client.GetShipmentAsync("APX-10000001");
        Assert.True(_harness.Circuit.IsOpen);

        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.OK, Shipment));
        await Task.Delay(ClientHarness.FastOptions.BreakDuration + TimeSpan.FromMilliseconds(200));
        var result = await _harness.Client.GetShipmentAsync("APX-10000001");

        Assert.True(result.Succeeded);
        Assert.Equal(CircuitState.Closed, _harness.Circuit.State);
        Assert.Contains("Circuit half-open", _harness.Logs.All);
        Assert.Contains("Circuit closed", _harness.Logs.All);
    }

    [Fact]
    public async Task The_default_settings_match_resilience_md()
    {
        var defaults = new ApexResilienceOptions();

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
