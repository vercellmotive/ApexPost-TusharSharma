namespace ApexPost.Courier.Offline.Domain;

/// <summary>What the courier did with the shipment. The member names are stored as text in the document.</summary>
public enum ScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed
}
