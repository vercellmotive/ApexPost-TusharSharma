using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace ApexPost.Courier.Core.Api;

public enum ApiFailureKind
{
    /// <summary>No failure.</summary>
    None,

    /// <summary>The device is offline or the server did not answer in time. Try again later.</summary>
    Unreachable,

    /// <summary>The circuit breaker is open after repeated failures, so no call was made.</summary>
    CircuitOpen,

    /// <summary>401: the token is missing, invalid or expired. The app signs out.</summary>
    Unauthorized,

    /// <summary>The server refused the request (4xx). Sending it again will not help.</summary>
    Rejected,

    /// <summary>The server failed (5xx) even after retries.</summary>
    ServerError,

    /// <summary>The server answered with something the app could not read.</summary>
    Malformed,
}

/// <summary>The outcome of an API call. The app never sees an exception for an HTTP or network failure.</summary>
public sealed record ApiResult<T>(T? Value, ApiFailureKind Failure, HttpStatusCode? StatusCode, string? Message)
{
    [MemberNotNullWhen(true, nameof(Value))]
    public bool Succeeded => Failure == ApiFailureKind.None && Value is not null;

    /// <summary>
    /// True when the same request may succeed later, so scans stay Pending. A reply the app cannot read counts too:
    /// a captive-portal page on public Wi-Fi answers 200 with HTML, and the scans must not be lost because of it.
    /// </summary>
    public bool IsTransient => Failure is ApiFailureKind.Unreachable or ApiFailureKind.CircuitOpen or ApiFailureKind.ServerError or ApiFailureKind.Malformed;

    public static ApiResult<T> Success(T value, HttpStatusCode status) => new(value, ApiFailureKind.None, status, null);

    public static ApiResult<T> Fail(ApiFailureKind failure, string message, HttpStatusCode? status = null) => new(default, failure, status, message);
}
