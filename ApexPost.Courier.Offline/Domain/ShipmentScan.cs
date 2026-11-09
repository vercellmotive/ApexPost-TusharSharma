namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// One scan of a shipment made by a courier. This is a plain class: nothing in the Domain namespace refers to
/// the database. How a scan becomes a JSON document is the job of the Storage namespace.
/// </summary>
public sealed record ShipmentScan
{
    /// <summary>Every document id is this prefix followed by a lower-case GUID: <c>scan::{guid}</c>.</summary>
    public const string IdPrefix = "scan::";

    public const int MaxTrackingNumberLength = 64;
    public const int MaxCourierIdLength = 32;
    public const int MaxLocationLength = 200;
    public const int MaxNotesLength = 500;

    public required string Id { get; init; }
    public required string TrackingNumber { get; init; }
    public required ScanType ScanType { get; init; }
    public required string CourierId { get; init; }
    public required string Location { get; init; }

    /// <summary>When the courier made the scan. Always UTC, so text ordering and time ordering agree.</summary>
    public required DateTimeOffset ScannedAtUtc { get; init; }

    public string? Notes { get; init; }
    public SyncStatus SyncStatus { get; init; } = SyncStatus.Pending;

    public static string NewId() => IdPrefix + Guid.NewGuid().ToString("D");

    /// <summary>True for <c>scan::</c> followed by a GUID in its canonical lower-case form.</summary>
    public static bool IsValidId(string? id) =>
        id is not null
        && id.StartsWith(IdPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(id.AsSpan(IdPrefix.Length), "D", out var guid)
        && id.AsSpan(IdPrefix.Length).SequenceEqual(guid.ToString("D"));

    /// <summary>Creates the scan a courier has just made: a new id, UTC time and syncStatus Pending.</summary>
    public static ShipmentScan CreatePending(
        string trackingNumber,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAt,
        string? notes = null)
    {
        var scan = new ShipmentScan
        {
            Id = NewId(),
            TrackingNumber = trackingNumber?.Trim() ?? string.Empty,
            ScanType = scanType,
            CourierId = courierId?.Trim() ?? string.Empty,
            Location = location?.Trim() ?? string.Empty,
            ScannedAtUtc = scannedAt.ToUniversalTime(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SyncStatus = SyncStatus.Pending
        };
        scan.Validate();
        return scan;
    }

    /// <summary>Throws <see cref="ArgumentException"/> when a field breaks a rule. The repository calls this before every write.</summary>
    public void Validate()
    {
        if (!IsValidId(Id))
        {
            throw new ArgumentException($"The id must look like {IdPrefix}{{guid}} with a lower-case GUID.", nameof(Id));
        }

        RequireCode(TrackingNumber, nameof(TrackingNumber), MaxTrackingNumberLength);
        RequireCode(CourierId, nameof(CourierId), MaxCourierIdLength);
        RequireText(Location, nameof(Location), MaxLocationLength, required: true);
        if (Notes is not null)
        {
            RequireText(Notes, nameof(Notes), MaxNotesLength, required: false);
        }

        if (!Enum.IsDefined(ScanType))
        {
            throw new ArgumentException("The scan type is not one of Pickup, InTransit, Delivered or Failed.", nameof(ScanType));
        }

        if (!Enum.IsDefined(SyncStatus))
        {
            throw new ArgumentException("The sync status is not one of Pending, Synced or Failed.", nameof(SyncStatus));
        }

        if (ScannedAtUtc == default)
        {
            throw new ArgumentException("The scan time is required.", nameof(ScannedAtUtc));
        }

        if (ScannedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The scan time must be UTC.", nameof(ScannedAtUtc));
        }
    }

    // Tracking numbers and courier ids are codes: ASCII letters and digits, with - and _ allowed.
    private static void RequireCode(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (value.Length > maxLength || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException($"{name} must be 1 to {maxLength} ASCII letters, digits, '-' or '_'.", name);
        }
    }

    private static void RequireText(string value, string name, int maxLength, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        if (value.Length > maxLength || value.Any(char.IsControl))
        {
            throw new ArgumentException($"{name} must be at most {maxLength} characters with no control characters.", name);
        }
    }
}
