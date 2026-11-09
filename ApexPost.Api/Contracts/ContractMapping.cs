using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Contracts;

/// <summary>Maps between the HTTP contract and the service. Controllers never pass request DTOs further in.</summary>
internal static class ContractMapping
{
    public static NewShipment ToNewShipment(this CreateShipmentRequest request) =>
        new(request.TrackingNumber!, request.OriginBranchCode!.Trim(), request.ToRecipient(), request.WeightKg!.Value);

    public static ShipmentDetails ToDetails(this UpdateShipmentRequest request) =>
        new(request.ToRecipient(), request.WeightKg!.Value);

    public static ShipmentQuery ToQuery(this ShipmentListQuery query) =>
        new(query.Page, query.PageSize, query.Status, query.City);

    public static IReadOnlyList<IncomingScan> ToIncomingScans(this ScanUploadRequest request) =>
        request.Scans!.Select(scan => new IncomingScan(
                scan.ScanId!,
                scan.ScanType!.Value,
                scan.CourierId!,
                scan.Location!,
                scan.ScannedAtUtc!.Value,
                scan.Notes,
                scan.RecipientSignatureName))
            .ToList();

    public static ShipmentResponse ToResponse(this ShipmentView view) => new(
        view.TrackingNumber,
        view.OriginBranchCode,
        view.Recipient.Name,
        view.Recipient.Phone,
        view.Recipient.Address,
        view.Recipient.City,
        Normalize(view.WeightKg),
        view.Status,
        view.CreatedAtUtc,
        view.UpdatedAtUtc);

    public static PagedResponse<TResponse> ToResponse<TView, TResponse>(this PagedResult<TView> page, Func<TView, TResponse> map) =>
        new(page.Items.Select(map).ToList(), page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    public static ScanResponse ToResponse(this TrackingScan scan) => new(
        scan.DocumentId,
        scan.ScanType,
        scan.CourierId,
        scan.Location,
        scan.ScannedAtUtc,
        scan.Notes,
        scan.RecipientSignatureName,
        scan.ReceivedAtUtc);

    public static ScanUploadResponse ToResponse(this ScanUploadOutcome outcome) => new(
        outcome.Accepted,
        outcome.DuplicatesSkipped,
        outcome.Rejected,
        outcome.Rejections.Select(rejection => new ScanRejectionResponse(rejection.ScanId, rejection.Reason)).ToList());

    // SQL Server returns decimal(6,3) as 2.500. Dropping the trailing zeros returns the weight as it was sent.
    internal static decimal Normalize(decimal value) => value / 1.000000000000000000000000000000000m;

    private static Recipient ToRecipient(this ShipmentDetailsRequest request) =>
        new(request.RecipientName!, request.RecipientPhone!, request.RecipientAddress!, request.City!);
}
