using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ApexPost.Courier.Core.Api;

/// <summary>
/// The typed client for ApexPost API v2. It is registered with AddHttpClient, so IHttpClientFactory owns the
/// HttpClient and its handlers: AuthHeaderHandler adds the token and the resilience handler retries, breaks the
/// circuit and times out. Every call returns an <see cref="ApiResult{T}"/>; HTTP and network failures never throw.
/// Only the method, path and status are logged, never a token or a request body.
/// </summary>
public sealed class ApexApiClient(HttpClient http, ILogger<ApexApiClient> logger)
{
    public const string OfflineMessage = "Server unavailable – working offline";

    /// <summary>Marks a request that is safe to send twice, so the resilience pipeline may retry it.</summary>
    public static readonly HttpRequestOptionsKey<bool> Idempotent = new("ApexPost.Idempotent");

    public Task<ApiResult<ShipmentDto>> GetShipmentAsync(string trackingNumber, CancellationToken ct = default) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}"), ApexJson.Context.ShipmentDto, ct);

    /// <summary>
    /// Uploads one shipment's scans. A POST is normally not retried, but this one is marked idempotent: every scan
    /// carries its Part A scan id and the server skips ids it already has, so a repeat cannot create duplicates.
    /// </summary>
    public Task<ApiResult<ScanUploadResponseDto>> UploadScansAsync(
        string trackingNumber,
        IReadOnlyList<ScanUploadItemDto> scans,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"api/v2/shipments/{Uri.EscapeDataString(trackingNumber)}/scans")
        {
            Content = JsonContent.Create(new ScanUploadRequestDto(scans), ApexJson.Context.ScanUploadRequestDto),
        };
        request.Options.Set(Idempotent, true);
        return SendAsync(request, ApexJson.Context.ScanUploadResponseDto, ct);
    }

#if DEBUG
    /// <summary>Debug builds only: asks the API's Development-only /dev/token endpoint for a courier token.</summary>
    public Task<ApiResult<DevelopmentTokenResponseDto>> RequestDevelopmentTokenAsync(string courierId, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "dev/token")
        {
            Content = JsonContent.Create(new DevelopmentTokenRequestDto(["Courier"], courierId), ApexJson.Context.DevelopmentTokenRequestDto),
        };
        return SendAsync(request, ApexJson.Context.DevelopmentTokenResponseDto, ct);
    }
#endif

    private async Task<ApiResult<T>> SendAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> type, CancellationToken ct)
    {
        using (request)
        {
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                logger.LogInformation("{Method} {Path} answered {Status}.", request.Method, request.RequestUri?.AbsolutePath, (int)response.StatusCode);
                return response.IsSuccessStatusCode
                    ? await ReadValueAsync(response, type, ct)
                    : await ReadFailureAsync<T>(response, ct);
            }
            catch (BrokenCircuitException)
            {
                return ApiResult<T>.Fail(ApiFailureKind.CircuitOpen, OfflineMessage);
            }
            catch (TimeoutRejectedException)
            {
                return ApiResult<T>.Fail(ApiFailureKind.Unreachable, "The server took too long to answer. Your scans are safe on this device.");
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning("{Method} {Path} could not reach the server: {Error}", request.Method, request.RequestUri?.AbsolutePath, exception.HttpRequestError);
                return ApiResult<T>.Fail(ApiFailureKind.Unreachable, "Could not reach the server. Your scans are safe on this device.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return ApiResult<T>.Fail(ApiFailureKind.Unreachable, "The server took too long to answer. Your scans are safe on this device.");
            }
        }
    }

    private static async Task<ApiResult<T>> ReadValueAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> type, CancellationToken ct)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync(type, ct);
            return value is null
                ? ApiResult<T>.Fail(ApiFailureKind.Malformed, "The server sent an empty reply.", response.StatusCode)
                : ApiResult<T>.Success(value, response.StatusCode);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Malformed, "The server sent a reply the app could not read.", response.StatusCode);
        }
    }

    private static async Task<ApiResult<T>> ReadFailureAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var problem = await ReadProblemAsync(response, ct);
        var status = response.StatusCode;
        var kind = status switch
        {
            HttpStatusCode.Unauthorized => ApiFailureKind.Unauthorized,
            HttpStatusCode.RequestTimeout => ApiFailureKind.Unreachable,
            >= HttpStatusCode.InternalServerError => ApiFailureKind.ServerError,
            _ => ApiFailureKind.Rejected,
        };
        return ApiResult<T>.Fail(kind, FriendlyMessage(status, problem), status);
    }

    // An error body should be ProblemDetails, but a proxy or a crash can send anything, so a body that is not
    // readable ProblemDetails is treated as no body.
    private static async Task<ProblemDto?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return response.Content.Headers.ContentLength == 0
                ? null
                : await response.Content.ReadFromJsonAsync(ApexJson.Context.ProblemDto, ct);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Turns ProblemDetails into a sentence a courier can act on.</summary>
    internal static string FriendlyMessage(HttpStatusCode status, ProblemDto? problem)
    {
        if (problem?.Errors is { Count: > 0 } errors)
        {
            return "Please check: " + string.Join(" ", errors.SelectMany(error => error.Value).Distinct().Take(3));
        }

        if (!string.IsNullOrWhiteSpace(problem?.Detail))
        {
            return problem.Detail;
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => "Your sign-in has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "You are not allowed to do that.",
            HttpStatusCode.NotFound => "The shipment was not found.",
            >= HttpStatusCode.InternalServerError => "The server had a problem. Your scans are safe on this device.",
            _ => problem?.Title ?? $"The server refused the request ({(int)status}).",
        };
    }
}
