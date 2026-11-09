namespace ApexPost.Tracking.Domain;

/// <summary>Who receives a shipment, and where.</summary>
public sealed record Recipient(string Name, string Phone, string Address, string City);
