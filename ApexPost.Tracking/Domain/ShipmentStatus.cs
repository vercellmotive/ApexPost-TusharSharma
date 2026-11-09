namespace ApexPost.Tracking.Domain;

/// <summary>Where a shipment is in its life. The allowed moves are in <see cref="Shipment.CanMoveTo"/>.</summary>
public enum ShipmentStatus
{
    Created,
    PickedUp,
    InTransit,
    OutForDelivery,
    Delivered,
    Failed,
}
