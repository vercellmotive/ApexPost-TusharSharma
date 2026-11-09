using System.Text.Json.Serialization;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Contracts.V2;

/// <summary>A shipment, as version 2 returns it.</summary>
/// <param name="TrackingNumber">The tracking number, APX- followed by eight digits.</param>
/// <param name="OriginBranchCode">The branch where the shipment was lodged.</param>
/// <param name="Recipient">Who receives the shipment, and where.</param>
/// <param name="WeightKg">Parcel weight in kilograms.</param>
/// <param name="Status">The status by name.</param>
/// <param name="LatestScan">The most recent courier scan, or null before the first scan.</param>
/// <param name="CreatedAtUtc">When the shipment was created.</param>
/// <param name="UpdatedAtUtc">When the shipment last changed.</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    string OriginBranchCode,
    RecipientResponse Recipient,
    decimal WeightKg,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShipmentStatus>))] ShipmentStatus Status,
    LatestScanResponse? LatestScan,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>The recipient of a shipment.</summary>
/// <param name="Name">Full name.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="Address">Street address.</param>
/// <param name="City">Destination city.</param>
public sealed record RecipientResponse(string Name, string Phone, string Address, string City);

/// <summary>A summary of the most recent scan of a shipment.</summary>
/// <param name="ScanType">What happened.</param>
/// <param name="Location">Where the scan was made.</param>
/// <param name="ScannedAtUtc">When the scan was made.</param>
public sealed record LatestScanResponse(
    [property: JsonConverter(typeof(JsonStringEnumConverter<ScanType>))] ScanType ScanType,
    string Location,
    DateTimeOffset ScannedAtUtc);
