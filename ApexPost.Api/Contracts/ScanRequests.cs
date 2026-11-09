using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApexPost.Api.Validation;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Contracts;

/// <summary>A batch of scans a courier device recorded offline (Part A) and now uploads.</summary>
public sealed class ScanUploadRequest
{
    public const int MaximumScans = 100;

    /// <summary>The scans, 1 to 100 per request.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(MaximumScans)]
    public IReadOnlyList<ScanUploadItem>? Scans { get; init; }
}

/// <summary>One offline scan. The fields match the Part A <c>ShipmentScan</c> document.</summary>
public sealed class ScanUploadItem : IValidatableObject
{
    /// <summary>The Part A document id, <c>scan::</c> followed by a lower-case GUID. Uploading the same id again is skipped.</summary>
    /// <example>scan::3f2504e0-4f89-41d3-9a0c-0305e82c3301</example>
    [Required]
    [RegularExpression(Tracking.Domain.ScanId.Pattern, ErrorMessage = "The {0} field must look like scan::{{Guid}} with a lower-case GUID.")]
    public string? ScanId { get; init; }

    /// <summary>What happened: Pickup, InTransit, Delivered or Failed.</summary>
    /// <example>Pickup</example>
    [Required]
    [EnumDataType(typeof(ScanType))]
    [JsonConverter(typeof(JsonStringEnumConverter<ScanType>))]
    public ScanType? ScanType { get; init; }

    /// <summary>The courier who made the scan.</summary>
    /// <example>CR-1001</example>
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]{1,32}$", ErrorMessage = "The {0} field must be 1 to 32 letters, digits, hyphens or underscores.")]
    public string? CourierId { get; init; }

    /// <summary>Where the scan was made.</summary>
    /// <example>Sydney depot</example>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string? Location { get; init; }

    /// <summary>When the scan was made, in UTC. It cannot be in the future.</summary>
    /// <example>2026-11-09T01:30:00Z</example>
    [Required]
    [NotInFuture]
    public DateTimeOffset? ScannedAtUtc { get; init; }

    /// <summary>Free-text notes from the courier.</summary>
    [StringLength(500)]
    public string? Notes { get; init; }

    /// <summary>Who signed for the parcel. Required for a Delivered scan.</summary>
    /// <example>Mia Fictional</example>
    [StringLength(100)]
    public string? RecipientSignatureName { get; init; }

    /// <summary>A custom rule: a Delivered scan must name the person who signed for the parcel.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScanType == Tracking.Domain.ScanType.Delivered && string.IsNullOrWhiteSpace(RecipientSignatureName))
        {
            yield return new ValidationResult(
                "A Delivered scan needs the name of the person who signed for it.",
                // MVC adds the member name to the error key as it is, so it is given the way the JSON spells it.
                [JsonNamingPolicy.CamelCase.ConvertName(nameof(RecipientSignatureName))]);
        }
    }
}
