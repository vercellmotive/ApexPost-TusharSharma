using System.Text.Json.Serialization;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Contracts;

/// <summary>A shipment.</summary>
/// <param name="TrackingNumber">The tracking number, APX- followed by eight digits.</param>
/// <param name="OriginBranchCode">The branch where the shipment was lodged.</param>
/// <param name="RecipientName">Full name of the recipient.</param>
/// <param name="RecipientPhone">Phone number of the recipient.</param>
/// <param name="RecipientAddress">Street address of the recipient.</param>
/// <param name="City">Destination city.</param>
/// <param name="WeightKg">Parcel weight in kilograms.</param>
/// <param name="Status">The status as a number: 0 Created, 1 PickedUp, 2 InTransit, 3 OutForDelivery, 4 Delivered, 5 Failed.</param>
/// <param name="CreatedAtUtc">When the shipment was created.</param>
/// <param name="UpdatedAtUtc">When the shipment last changed.</param>
public sealed record ShipmentResponse(
    string TrackingNumber,
    string OriginBranchCode,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddress,
    string City,
    decimal WeightKg,
    ShipmentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>One page of results.</summary>
/// <param name="Items">The results on this page.</param>
/// <param name="Page">This page number, starting at 1.</param>
/// <param name="PageSize">The largest number of results on a page.</param>
/// <param name="TotalCount">Results across all pages.</param>
/// <param name="TotalPages">Pages in all.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

/// <summary>A scan in a shipment's history.</summary>
/// <param name="ScanId">The Part A document id, scan::{Guid}.</param>
/// <param name="ScanType">What happened.</param>
/// <param name="CourierId">The courier who made the scan.</param>
/// <param name="Location">Where the scan was made.</param>
/// <param name="ScannedAtUtc">When the scan was made.</param>
/// <param name="Notes">Notes from the courier.</param>
/// <param name="RecipientSignatureName">Who signed for a delivered parcel.</param>
/// <param name="ReceivedAtUtc">When the server received the scan.</param>
public sealed record ScanResponse(
    string ScanId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ScanType>))] ScanType ScanType,
    string CourierId,
    string Location,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    string? RecipientSignatureName,
    DateTimeOffset ReceivedAtUtc);

/// <summary>What happened to each scan in an upload.</summary>
/// <param name="Accepted">Scans stored by this upload.</param>
/// <param name="DuplicatesSkipped">Scans already stored for this shipment, so nothing was written for them.</param>
/// <param name="Rejected">Scans that were refused. They are listed in <paramref name="Rejections"/>.</param>
/// <param name="Rejections">Each refused scan and why.</param>
public sealed record ScanUploadResponse(int Accepted, int DuplicatesSkipped, int Rejected, IReadOnlyList<ScanRejectionResponse> Rejections);

/// <summary>A scan the server refused.</summary>
/// <param name="ScanId">The scan id as it was sent.</param>
/// <param name="Reason">Why the scan was refused.</param>
public sealed record ScanRejectionResponse(string ScanId, string Reason);
