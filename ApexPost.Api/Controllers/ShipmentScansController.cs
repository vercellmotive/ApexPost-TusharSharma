using ApexPost.Api.Contracts;
using ApexPost.Api.Errors;
using ApexPost.Tracking.Application;
using ApexPost.Api.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>
/// The scan history of a shipment, and the upload of scans recorded offline by courier devices. The scan contract
/// did not change in version 2, so one controller serves both versions.
/// </summary>
[ApiController]
[ApiVersion(1.0, Deprecated = true)]
[ApiVersion(2.0)]
[Route("api/v{version:apiVersion}/shipments/{trackingNumber:trackingNumber}/scans")]
[Tags("Scans")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class ShipmentScansController(IShipmentService shipments) : ControllerBase
{
    /// <summary>Upload offline scans.</summary>
    /// <remarks>Uploads scans recorded offline. Safe to repeat: a scan id that is already stored is skipped.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">One to 100 scans.</param>
    /// <response code="200">How many scans were accepted, skipped as duplicates and rejected, with the reasons.</response>
    /// <response code="403">The token is not a Courier token, or a scan carries another courier's id.</response>
    [EndpointSummary("Upload offline scans")]
    [HttpPost(Name = "UploadScans")]
    [Authorize(Roles = ApexPostRoles.Courier)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [Consumes("application/json")]
    [ProducesResponseType<ScanUploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<ActionResult<ScanUploadResponse>> Upload(string trackingNumber, ScanUploadRequest request, CancellationToken ct)
    {
        var courierId = User.FindFirst(ApexPostRoles.CourierIdClaim)?.Value ?? string.Empty;
        var result = await shipments.UploadScansAsync(trackingNumber, request.ToIncomingScans(), courierId, ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Get scan history.</summary>
    /// <remarks>Gets the scan history of a shipment, oldest first.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    [EndpointSummary("Get scan history")]
    [HttpGet(Name = "ListScans")]
    [ProducesResponseType<IReadOnlyList<ScanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> List(string trackingNumber, CancellationToken ct)
    {
        var result = await shipments.GetScansAsync(trackingNumber, ct);
        return result.Succeeded ? Ok(result.Value.Select(scan => scan.ToResponse()).ToList()) : this.ToProblem(result.Error);
    }
}
