namespace ApexPost.Tracking.Domain;

/// <summary>
/// A shipment that customers and couriers track by its tracking number. It owns the status rules:
/// Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed.
/// </summary>
public sealed class Shipment
{
    public const decimal MinimumWeightKg = 0.1m;
    public const decimal MaximumWeightKg = 70m;

    // Each status may move only to the next one. OutForDelivery ends in Delivered or Failed, and those two are final.
    private static readonly IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> NextStatuses =
        new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Created] = [ShipmentStatus.PickedUp],
            [ShipmentStatus.PickedUp] = [ShipmentStatus.InTransit],
            [ShipmentStatus.InTransit] = [ShipmentStatus.OutForDelivery],
            [ShipmentStatus.OutForDelivery] = [ShipmentStatus.Delivered, ShipmentStatus.Failed],
            [ShipmentStatus.Delivered] = [],
            [ShipmentStatus.Failed] = [],
        };

    // For EF Core.
    private Shipment()
    {
    }

    public int Id { get; private set; }
    public string TrackingNumber { get; private set; } = string.Empty;
    public string OriginBranchCode { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public string RecipientPhone { get; private set; } = string.Empty;
    public string RecipientAddress { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public decimal WeightKg { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Recipient Recipient => new(RecipientName, RecipientPhone, RecipientAddress, City);

    /// <summary>Delivered and Failed are final: nothing about the shipment changes after that.</summary>
    public bool IsClosed => NextStatuses[Status].Length == 0;

    /// <summary>A shipment can be deleted only before a courier has picked it up.</summary>
    public bool CanBeDeleted => Status == ShipmentStatus.Created;

    public static Shipment Create(
        string trackingNumber,
        string originBranchCode,
        Recipient recipient,
        decimal weightKg,
        DateTimeOffset nowUtc)
    {
        if (!Domain.TrackingNumber.IsValid(trackingNumber))
        {
            throw new ArgumentException($"Tracking number must match {Domain.TrackingNumber.Pattern}.", nameof(trackingNumber));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(originBranchCode);
        var shipment = new Shipment
        {
            TrackingNumber = trackingNumber,
            OriginBranchCode = originBranchCode,
            Status = ShipmentStatus.Created,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
        };
        shipment.ReplaceDetails(recipient, weightKg, nowUtc);
        return shipment;
    }

    public static bool CanMove(ShipmentStatus from, ShipmentStatus to) => NextStatuses[from].Contains(to);

    public static IReadOnlyList<ShipmentStatus> AllowedNext(ShipmentStatus from) => NextStatuses[from];

    public bool CanMoveTo(ShipmentStatus next) => CanMove(Status, next);

    public void MoveTo(ShipmentStatus next, DateTimeOffset nowUtc)
    {
        if (!CanMoveTo(next))
        {
            throw new InvalidOperationException($"A shipment cannot move from {Status} to {next}.");
        }

        Status = next;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }

    /// <summary>Replaces the editable details. A closed shipment keeps the details it was delivered or failed with.</summary>
    public void ReplaceDetails(Recipient recipient, decimal weightKg, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient.Phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient.Address);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient.City);
        if (weightKg is < MinimumWeightKg or > MaximumWeightKg)
        {
            throw new ArgumentOutOfRangeException(nameof(weightKg), weightKg, $"Weight must be between {MinimumWeightKg} and {MaximumWeightKg} kg.");
        }

        if (IsClosed)
        {
            throw new InvalidOperationException($"A {Status} shipment cannot be changed.");
        }

        RecipientName = recipient.Name.Trim();
        RecipientPhone = recipient.Phone.Trim();
        RecipientAddress = recipient.Address.Trim();
        City = recipient.City.Trim();
        WeightKg = weightKg;
        UpdatedAtUtc = nowUtc.ToUniversalTime();
    }
}
