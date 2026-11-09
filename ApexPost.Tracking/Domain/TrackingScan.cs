namespace ApexPost.Tracking.Domain;

/// <summary>
/// A scan a courier recorded, usually offline, and uploaded later. <see cref="ScanId"/> is the GUID from the Part A
/// document id <c>scan::{Guid}</c>, so uploading the same scan twice finds the row that is already there.
/// </summary>
public sealed class TrackingScan
{
    // For EF Core.
    private TrackingScan()
    {
    }

    public Guid ScanId { get; private set; }
    public int ShipmentId { get; private set; }
    public ScanType ScanType { get; private set; }
    public string CourierId { get; private set; } = string.Empty;
    public string Location { get; private set; } = string.Empty;
    public DateTimeOffset ScannedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public string? RecipientSignatureName { get; private set; }

    /// <summary>When the server stored the scan. Scans can arrive hours after they were made.</summary>
    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string DocumentId => Domain.ScanId.Format(ScanId);

    public static TrackingScan Record(
        Guid scanId,
        int shipmentId,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAtUtc,
        string? notes,
        string? recipientSignatureName,
        DateTimeOffset receivedAtUtc)
    {
        if (scanId == Guid.Empty)
        {
            throw new ArgumentException("A scan needs an id.", nameof(scanId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        return new TrackingScan
        {
            ScanId = scanId,
            ShipmentId = shipmentId,
            ScanType = scanType,
            CourierId = courierId.Trim(),
            Location = location.Trim(),
            ScannedAtUtc = scannedAtUtc.ToUniversalTime(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            RecipientSignatureName = string.IsNullOrWhiteSpace(recipientSignatureName) ? null : recipientSignatureName.Trim(),
            ReceivedAtUtc = receivedAtUtc.ToUniversalTime(),
        };
    }
}
