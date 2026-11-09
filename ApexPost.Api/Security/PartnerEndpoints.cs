using ApexPost.Api.Contracts.V2;
using ApexPost.Tracking.Application;
using Asp.Versioning;

namespace ApexPost.Api.Security;

/// <summary>The read-only partner route. It uses an API key instead of a bearer token.</summary>
internal static class PartnerEndpoints
{
    public static void MapPartnerEndpoints(this WebApplication app)
    {
        var partner = app.NewVersionedApi("Partner");
        partner.MapGet("/api/v{version:apiVersion}/partner/shipments/{trackingNumber:trackingNumber}", GetShipmentAsync)
            .HasApiVersion(new ApiVersion(2, 0))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiting.PartnerPolicy)
            .AddEndpointFilter<PartnerApiKeyFilter>()
            .WithName("GetPartnerShipment")
            .WithTags("Partner")
            .WithSummary("Track a shipment as a partner")
            .WithDescription("Read-only tracking for partner systems. Send the partner API key in the X-Api-Key header.")
            .Produces<PartnerShipmentResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static async Task<IResult> GetShipmentAsync(string trackingNumber, IShipmentService shipments, CancellationToken ct)
    {
        var result = await shipments.GetAsync(trackingNumber, ct);
        if (!result.Succeeded)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: result.Error.Title, detail: result.Error.Detail);
        }

        var view = result.Value;
        return TypedResults.Ok(new PartnerShipmentResponse(
            view.TrackingNumber,
            view.Status,
            view.Recipient.City,
            view.LatestScan is { } scan ? new LatestScanResponse(scan.ScanType, scan.Location, scan.ScannedAtUtc) : null,
            view.UpdatedAtUtc));
    }
}
