using ApexPost.Tracking.Application;

namespace ApexPost.Api.Contracts.V2;

/// <summary>The version 2 response mapping. Requests and the service are the same as in version 1.</summary>
internal static class ContractMappingV2
{
    public static ShipmentResponse ToResponseV2(this ShipmentView view) => new(
        view.TrackingNumber,
        view.OriginBranchCode,
        new RecipientResponse(view.Recipient.Name, view.Recipient.Phone, view.Recipient.Address, view.Recipient.City),
        ContractMapping.Normalize(view.WeightKg),
        view.Status,
        view.LatestScan is { } scan ? new LatestScanResponse(scan.ScanType, scan.Location, scan.ScannedAtUtc) : null,
        view.CreatedAtUtc,
        view.UpdatedAtUtc);
}
