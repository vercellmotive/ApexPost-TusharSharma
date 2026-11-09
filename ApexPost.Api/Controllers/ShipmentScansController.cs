using ApexPost.Api.Contracts;
using ApexPost.Api.Errors;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>The scan history of a shipment, and the upload of scans recorded offline by courier devices.</summary>
[ApiController]
[Route("api/shipments/{trackingNumber:trackingNumber}/scans")]
public sealed class ShipmentScansController(IShipmentService shipments) : ControllerBase
{
    /// <summary>Uploads scans recorded offline. Safe to repeat: a scan id that is already stored is skipped.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">One to 100 scans.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    /// <response code="200">How many scans were accepted, skipped as duplicates and rejected, with the reasons.</response>
    [HttpPost(Name = "UploadScans")]
    [Consumes("application/json")]
    [ProducesResponseType<ScanUploadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<ActionResult<ScanUploadResponse>> Upload(string trackingNumber, ScanUploadRequest request, CancellationToken ct)
    {
        var result = await shipments.UploadScansAsync(trackingNumber, request.ToIncomingScans(), ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Gets the scan history of a shipment, oldest first.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet(Name = "ListScans")]
    [ProducesResponseType<IReadOnlyList<ScanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<ScanResponse>>> List(string trackingNumber, CancellationToken ct)
    {
        var result = await shipments.GetScansAsync(trackingNumber, ct);
        return result.Succeeded ? Ok(result.Value.Select(scan => scan.ToResponse()).ToList()) : this.ToProblem(result.Error);
    }
}
