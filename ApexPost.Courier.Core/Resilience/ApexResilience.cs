using System.Net;
using ApexPost.Courier.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace ApexPost.Courier.Core.Resilience;

/// <summary>
/// The Polly pipeline around every API call, from the outside in: an overall timeout, retries with exponential
/// backoff and jitter, a circuit breaker, and a per-attempt timeout. Every retry and every circuit change is logged.
/// </summary>
public static class ApexResilience
{
    public const string PipelineName = "apex-api";

    public static IHttpClientBuilder AddApexResilience(this IHttpClientBuilder builder, ApexResilienceOptions? settings = null)
    {
        var options = settings ?? new ApexResilienceOptions();
        builder.Services.AddSingleton<CircuitStateMonitor>();
        builder.AddResilienceHandler(PipelineName, (pipeline, context) =>
        {
            var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ApexPost.Courier.Resilience");
            var circuit = context.ServiceProvider.GetRequiredService<CircuitStateMonitor>();

            pipeline.AddTimeout(new HttpTimeoutStrategyOptions
            {
                Timeout = options.TotalTimeout,
                OnTimeout = args =>
                {
                    logger.LogWarning("Request gave up after the overall timeout of {Timeout}.", args.Timeout);
                    return default;
                },
            });

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.RetryBaseDelay,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome) && IsIdempotent(args.Context.GetRequestMessage())),
                OnRetry = args =>
                {
                    var request = args.Context.GetRequestMessage();
                    logger.LogWarning(
                        "Retry {Attempt} of {Max} for {Method} {Path} in {Delay:0.00} s, after {Reason}.",
                        args.AttemptNumber + 1,
                        options.MaxRetryAttempts,
                        request?.Method,
                        request?.RequestUri?.AbsolutePath,
                        args.RetryDelay.TotalSeconds,
                        Describe(args.Outcome));
                    return default;
                },
            });

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                MinimumThroughput = options.MinimumThroughput,
                SamplingDuration = options.SamplingDuration,
                BreakDuration = options.BreakDuration,
                ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                OnOpened = args =>
                {
                    circuit.Set(CircuitState.Open);
                    logger.LogError("Circuit opened for {Duration} after {Reason}. {Banner}.", args.BreakDuration, Describe(args.Outcome), ApexApiClient.OfflineMessage);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    circuit.Set(CircuitState.HalfOpen);
                    logger.LogInformation("Circuit half-open: the next request tests the server.");
                    return default;
                },
                OnClosed = _ =>
                {
                    circuit.Set(CircuitState.Closed);
                    logger.LogInformation("Circuit closed: the server is answering again.");
                    return default;
                },
            });

            pipeline.AddTimeout(new HttpTimeoutStrategyOptions { Timeout = options.AttemptTimeout });
        });
        return builder;
    }

    /// <summary>Network errors, timeouts, 408 and 5xx. Never a 4xx: the same request would fail the same way.</summary>
    internal static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || outcome.Result is { StatusCode: HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError };

    /// <summary>GET, PUT, DELETE and HEAD by definition, and any request the client marked idempotent.</summary>
    internal static bool IsIdempotent(HttpRequestMessage? request) =>
        request is not null
        && (request.Method == HttpMethod.Get
            || request.Method == HttpMethod.Head
            || request.Method == HttpMethod.Put
            || request.Method == HttpMethod.Delete
            || (request.Options.TryGetValue(ApexApiClient.Idempotent, out var marked) && marked));

    private static string Describe(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is { } exception ? exception.GetType().Name : $"HTTP {(int)outcome.Result!.StatusCode}";
}
