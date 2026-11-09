using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Courier.Core.Api;

// The app's own copies of the v2 contract. They are small, tolerant of fields the server adds later, and
// serialized through the source-generated ApexJsonContext, so no reflection is needed on the device.

public enum ShipmentStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
}

public enum ScanKind
{
    Pickup,
    InTransit,
    Delivered,
    Failed,
}

public sealed record RecipientDto(string? Name, string? Phone, string? Address, string? City);

public sealed record LatestScanDto(ScanKind ScanType, string? Location, DateTimeOffset ScannedAtUtc);

public sealed record ShipmentDto(
    string TrackingNumber,
    RecipientDto? Recipient,
    decimal WeightKg,
    ShipmentStatus Status,
    LatestScanDto? LatestScan,
    DateTimeOffset UpdatedAtUtc);

public sealed record ScanUploadItemDto(
    string ScanId,
    ScanKind ScanType,
    string CourierId,
    string Location,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    string? RecipientSignatureName);

public sealed record ScanUploadRequestDto(IReadOnlyList<ScanUploadItemDto> Scans);

public sealed record ScanRejectionDto(string ScanId, string? Reason);

public sealed record ScanUploadResponseDto(int Accepted, int DuplicatesSkipped, int Rejected, IReadOnlyList<ScanRejectionDto>? Rejections);

/// <summary>ProblemDetails and ValidationProblemDetails in one shape: errors is null for a plain problem.</summary>
public sealed record ProblemDto(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    IReadOnlyDictionary<string, string[]>? Errors);

public sealed record DevelopmentTokenRequestDto(IReadOnlyList<string> Roles, string CourierId);

public sealed record DevelopmentTokenResponseDto(string AccessToken, DateTimeOffset ExpiresAtUtc);

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.Strict,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShipmentDto))]
[JsonSerializable(typeof(ScanUploadRequestDto))]
[JsonSerializable(typeof(ScanUploadResponseDto))]
[JsonSerializable(typeof(ProblemDto))]
[JsonSerializable(typeof(DevelopmentTokenRequestDto))]
[JsonSerializable(typeof(DevelopmentTokenResponseDto))]
public sealed partial class ApexJsonContext : JsonSerializerContext;

/// <summary>
/// The one set of JSON options the app uses, matching the v2 API: camelCase, case-insensitive reading, enums as
/// names, and unknown fields ignored. Backed by the source-generated <see cref="ApexJsonContext"/>.
/// </summary>
public static class ApexJson
{
    public static ApexJsonContext Context { get; } = ApexJsonContext.Default;

    public static JsonSerializerOptions Options => Context.Options;
}
