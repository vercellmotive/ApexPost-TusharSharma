using ApexPost.Tracking.Domain;

namespace ApexPost.Tracking.Application;

public sealed record NewShipment(string TrackingNumber, string OriginBranchCode, Recipient Recipient, decimal WeightKg);

public sealed record ShipmentDetails(Recipient Recipient, decimal WeightKg);

/// <param name="City">Matched without regard to case.</param>
public sealed record ShipmentQuery(int Page, int PageSize, ShipmentStatus? Status, string? City)
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record ScanSummary(ScanType ScanType, string Location, DateTimeOffset ScannedAtUtc);

/// <summary>A shipment as it is read back, with its most recent scan if it has one.</summary>
public sealed record ShipmentView(
    string TrackingNumber,
    string OriginBranchCode,
    Recipient Recipient,
    decimal WeightKg,
    ShipmentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ScanSummary? LatestScan)
{
    public static ShipmentView From(Shipment shipment, ScanSummary? latestScan) => new(
        shipment.TrackingNumber,
        shipment.OriginBranchCode,
        shipment.Recipient,
        shipment.WeightKg,
        shipment.Status,
        shipment.CreatedAtUtc,
        shipment.UpdatedAtUtc,
        latestScan);
}

/// <summary>One scan in an upload. <paramref name="ScanId"/> is the Part A document id, <c>scan::{Guid}</c>.</summary>
public sealed record IncomingScan(
    string ScanId,
    ScanType ScanType,
    string CourierId,
    string Location,
    DateTimeOffset ScannedAtUtc,
    string? Notes,
    string? RecipientSignatureName);

public sealed record ScanRejection(string ScanId, string Reason);

public sealed record ScanUploadOutcome(int Accepted, int DuplicatesSkipped, IReadOnlyList<ScanRejection> Rejections)
{
    public int Rejected => Rejections.Count;
}
