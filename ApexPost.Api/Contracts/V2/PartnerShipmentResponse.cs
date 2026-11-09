using System.Text.Json.Serialization;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Contracts.V2;

/// <summary>
/// What a partner may see about a shipment: where it is, not who receives it. The recipient's name, phone and
/// address stay private.
/// </summary>
/// <param name="TrackingNumber">The tracking number.</param>
/// <param name="Status">The status by name.</param>
/// <param name="DestinationCity">The destination city.</param>
/// <param name="LatestScan">The most recent courier scan, or null before the first scan.</param>
/// <param name="UpdatedAtUtc">When the shipment last changed.</param>
public sealed record PartnerShipmentResponse(
    string TrackingNumber,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ShipmentStatus>))] ShipmentStatus Status,
    string DestinationCity,
    LatestScanResponse? LatestScan,
    DateTimeOffset UpdatedAtUtc);
