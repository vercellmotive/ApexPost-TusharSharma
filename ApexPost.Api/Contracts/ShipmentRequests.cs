using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ApexPost.Api.Validation;
using ApexPost.Tracking.Domain;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Contracts;

/// <summary>The recipient and parcel details that can be replaced later. Shared by create and update.</summary>
public abstract class ShipmentDetailsRequest
{
    /// <summary>Full name of the person receiving the shipment.</summary>
    /// <example>Mia Fictional</example>
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string? RecipientName { get; init; }

    /// <summary>Recipient phone number, 8 to 15 digits, optionally starting with +.</summary>
    /// <example>+61 2 5550 1234</example>
    [Required]
    [StringLength(20)]
    [PhoneNumber]
    public string? RecipientPhone { get; init; }

    /// <summary>Street address of the recipient.</summary>
    /// <example>12 Example Lane</example>
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string? RecipientAddress { get; init; }

    /// <summary>Destination city.</summary>
    /// <example>Sydney</example>
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string? City { get; init; }

    /// <summary>Parcel weight in kilograms, from 0.1 to 70.</summary>
    /// <example>2.5</example>
    [Required]
    [Range(typeof(decimal), "0.1", "70", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal? WeightKg { get; init; }
}

/// <summary>A new shipment lodged at a branch counter.</summary>
public sealed class CreateShipmentRequest : ShipmentDetailsRequest
{
    /// <summary>The tracking number printed on the label: APX- followed by eight digits.</summary>
    /// <example>APX-10000001</example>
    [Required]
    [TrackingNumber]
    public string? TrackingNumber { get; init; }

    /// <summary>Code of the Week 3 branch where the shipment was lodged. The branch must exist and be open.</summary>
    /// <example>B001</example>
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string? OriginBranchCode { get; init; }
}

/// <summary>Replaces the editable details of a shipment. The tracking number, origin branch and status do not change.</summary>
public sealed class UpdateShipmentRequest : ShipmentDetailsRequest;

/// <summary>Moves a shipment to its next status.</summary>
public sealed class UpdateStatusRequest
{
    /// <summary>
    /// The new status, by name or number. Allowed moves: Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed.
    /// </summary>
    /// <example>PickedUp</example>
    [Required]
    [EnumDataType(typeof(ShipmentStatus))]
    [JsonConverter(typeof(JsonStringEnumConverter<ShipmentStatus>))]
    public ShipmentStatus? Status { get; init; }
}

/// <summary>Paging and filters for the shipment list.</summary>
public sealed class ShipmentListQuery
{
    /// <summary>Page number, starting at 1.</summary>
    [FromQuery(Name = "page")]
    [Description("Page number, starting at 1.")]
    [Range(1, 100_000)]
    public int Page { get; init; } = 1;

    /// <summary>Shipments per page, from 1 to 50. The default is 20.</summary>
    [FromQuery(Name = "pageSize")]
    [Description("Shipments per page, from 1 to 50. The default is 20.")]
    [Range(1, Tracking.Application.ShipmentQuery.MaximumPageSize)]
    public int PageSize { get; init; } = Tracking.Application.ShipmentQuery.DefaultPageSize;

    /// <summary>Only shipments in this status.</summary>
    [FromQuery(Name = "status")]
    [Description("Only shipments in this status.")]
    [EnumDataType(typeof(ShipmentStatus))]
    public ShipmentStatus? Status { get; init; }

    /// <summary>Only shipments to this city. Case does not matter.</summary>
    [FromQuery(Name = "city")]
    [Description("Only shipments to this city. Case does not matter.")]
    [StringLength(100)]
    public string? City { get; init; }
}
