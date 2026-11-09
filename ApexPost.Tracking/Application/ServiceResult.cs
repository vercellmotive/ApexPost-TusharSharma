using System.Diagnostics.CodeAnalysis;

namespace ApexPost.Tracking.Application;

public enum ServiceErrorKind
{
    /// <summary>The input breaks a rule that needs stored data to check, such as an unknown branch.</summary>
    Validation,
    NotFound,
    Conflict,

    /// <summary>The caller is signed in but may not do this, such as uploading another courier's scans.</summary>
    Forbidden,
}

/// <param name="Field">For a validation error, the input field it belongs to.</param>
public sealed record ServiceError(ServiceErrorKind Kind, string Title, string Detail, string? Field = null)
{
    public static ServiceError ShipmentNotFound(string trackingNumber) =>
        new(ServiceErrorKind.NotFound, "Shipment not found", $"No shipment has tracking number {trackingNumber}.");
}

/// <summary>The outcome of a use case: a value, or the reason there is none. Expected failures are not exceptions.</summary>
public readonly record struct ServiceResult<T>
{
    private ServiceResult(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public ServiceError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;

    public static ServiceResult<T> Success(T value) => new(value, null);

    public static implicit operator ServiceResult<T>(ServiceError error) => new(default, error);
}
