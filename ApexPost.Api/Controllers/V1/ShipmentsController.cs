using ApexPost.Api.Contracts;
using ApexPost.Api.Errors;
using ApexPost.Tracking.Application;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Controllers.V1;

/// <summary>
/// Version 1 of the shipment endpoints: the Part B contract, unchanged. It is deprecated in favour of version 2.
/// Each action binds input, calls the service and returns a result.
/// </summary>
[ApiController]
[ApiVersion(1.0, Deprecated = true)]
[Route("api/v{version:apiVersion}/shipments")]
[Tags("Shipments")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class ShipmentsController(IShipmentService shipments) : ControllerBase
{
    /// <summary>List shipments.</summary>
    /// <remarks>Lists shipments, newest first, a page at a time.</remarks>
    /// <param name="query">Paging and optional status and city filters.</param>
    [EndpointSummary("List shipments")]
    [HttpGet(Name = "ListShipments")]
    [ProducesResponseType<PagedResponse<ShipmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<ActionResult<PagedResponse<ShipmentResponse>>> List([FromQuery] ShipmentListQuery query, CancellationToken ct)
    {
        var page = await shipments.ListAsync(query.ToQuery(), ct);
        return Ok(page.ToResponse(view => view.ToResponse()));
    }

    /// <summary>Get a shipment.</summary>
    /// <remarks>Gets one shipment.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    [EndpointSummary("Get a shipment")]
    [HttpGet("{trackingNumber:trackingNumber}", Name = "GetShipment")]
    [ProducesResponseType<ShipmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ShipmentResponse>> Get(string trackingNumber, CancellationToken ct)
    {
        var result = await shipments.GetAsync(trackingNumber, ct);
        return result.Succeeded ? Ok(result.Value.ToResponse()) : this.ToProblem(result.Error);
    }

    /// <summary>Create a shipment.</summary>
    /// <remarks>Creates a shipment in Created status.</remarks>
    /// <param name="request">The tracking number, origin branch, recipient and weight.</param>
    /// <response code="201">The shipment was created. The Location header is its URL.</response>
    /// <response code="409">A shipment with this tracking number already exists.</response>
    [EndpointSummary("Create a shipment")]
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

    /// <summary>Replace shipment details.</summary>
    /// <remarks>Replaces the recipient details and weight of a shipment.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">The new details. Every field is replaced.</param>
    /// <response code="409">The shipment is Delivered or Failed, or another request changed it first.</response>
    [EndpointSummary("Replace shipment details")]
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

    /// <summary>Change shipment status.</summary>
    /// <remarks>Changes only the status of a shipment.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <param name="request">The next status.</param>
    /// <response code="409">The move is not allowed from the current status, or another request changed the shipment first.</response>
    [EndpointSummary("Change shipment status")]
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

    /// <summary>Delete a shipment.</summary>
    /// <remarks>Deletes a shipment that is still in Created status.</remarks>
    /// <param name="trackingNumber">The tracking number, such as APX-10000001.</param>
    /// <response code="204">The shipment and its scans were deleted.</response>
    /// <response code="409">The shipment has moved past Created.</response>
    [EndpointSummary("Delete a shipment")]
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
