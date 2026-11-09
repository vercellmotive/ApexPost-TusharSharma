namespace ApexPost.Tracking.Domain;

/// <summary>The scan types a courier device records. The names match the Part A offline documents.</summary>
public enum ScanType
{
    Pickup,
    InTransit,
    Delivered,
    Failed,
}
