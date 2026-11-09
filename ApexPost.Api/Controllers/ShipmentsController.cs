using ApexPost.Api.Contracts;
using ApexPost.Api.Errors;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers;

/// <summary>Shipments, found by their tracking number. Each action binds input, calls the service and returns a result.</summary>
[ApiController]
[Route("api/shipments")]
public sealed class ShipmentsController(IShipmentService shipments) : ControllerBase
{
    /// <summary>Lists shipments, newest first, a page at a time.</summary>
    /// <param name="query">Paging and optional status and city filters.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet(Name = "ListShipments")]
    [ProducesResponseType<PagedResponse<ShipmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ShipmentResponse>>> List([FromQuery] ShipmentListQuery query, CancellationToken ct)
    {
        var page = await shipments.ListAsync(query.ToQuery(), ct);
        return Ok(page.ToResponse(view => view.ToResponse()));
    }

    /// <summary>Gets one shipment.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetShipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> Get(string trackingNumber, CancellationToken ct)
    {
        var result = await shipments.GetAsync(trackingNumber, ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Creates a shipment in Created status.</summary>
    /// <param name="request">The tracking number, origin branch, recipient and weight.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    /// <response code="201">The shipment was created. The Location header is its URL.</response>
    /// <response code="409">A shipment with this tracking number already exists.</response>
    [HttpPost(Name = "CreateShipment")]
    [Consumes("application/json")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> Create(CreateShipmentRequest request, CancellationToken ct)
    {
        var result = await shipments.CreateAsync(request.ToNewShipment(), ct);
        return result.Succeeded
            ? CreatedAtRoute("GetShipment", new { trackingNumber = result.Value.TrackingNumber }, result.Value.ToResponse())
            : this.ToProblem(result.Error);
    }

    /// <summary>Replaces the recipient details and weight of a shipment.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">The new details. Every field is replaced.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    /// <response code="409">The shipment is Delivered or Failed, or another request changed it first.</response>
    [HttpPut("{trackingNumber:trackingNumber}", Name = "UpdateShipment")]
    [Consumes("application/json")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> Update(string trackingNumber, UpdateShipmentRequest request, CancellationToken ct)
    {
        var result = await shipments.ReplaceDetailsAsync(trackingNumber, request.ToDetails(), ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Changes only the status of a shipment.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">The next status.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    /// <response code="409">The move is not allowed from the current status, or another request changed the shipment first.</response>
    [HttpPatch("{trackingNumber:trackingNumber}/status", Name = "UpdateShipmentStatus")]
    [Consumes("application/json")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> UpdateStatus(string trackingNumber, UpdateStatusRequest request, CancellationToken ct)
    {
        var result = await shipments.ChangeStatusAsync(trackingNumber, request.Status!.Value, ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Deletes a shipment that is still in Created status.</summary>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="ct">Cancelled when the client disconnects.</param>
    /// <response code="204">The shipment and its scans were deleted.</response>
    /// <response code="409">The shipment has moved past Created.</response>
    [HttpDelete("{trackingNumber:trackingNumber}", Name = "DeleteShipment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(string trackingNumber, CancellationToken ct)
    {
        var result = await shipments.DeleteAsync(trackingNumber, ct);
        return result.Succeeded ? NoContent() : this.ToProblem(result.Error);
    }
}
