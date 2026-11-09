namespace ApexPost.Tracking.Application;

/// <summary>The shipment tracking use cases. Every business rule of the API lives behind this interface.</summary>
public interface IShipmentService
{
    Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default);

    Task<ServiceResult<ShipmentView>> GetAsync(string trackingNumber, CancellationToken ct = default);

    Task<ServiceResult<ShipmentView>> CreateAsync(NewShipment request, CancellationToken ct = default);

    Task<ServiceResult<ShipmentView>> ReplaceDetailsAsync(string trackingNumber, ShipmentDetails details, CancellationToken ct = default);

    Task<ServiceResult<ShipmentView>> ChangeStatusAsync(string trackingNumber, Domain.ShipmentStatus status, CancellationToken ct = default);

    Task<ServiceResult<Done>> DeleteAsync(string trackingNumber, CancellationToken ct = default);

    Task<ServiceResult<IReadOnlyList<Domain.TrackingScan>>> GetScansAsync(string trackingNumber, CancellationToken ct = default);

    /// <param name="uploadingCourierId">The courier id of the signed-in courier. Every scan must carry it.</param>
    Task<ServiceResult<ScanUploadOutcome>> UploadScansAsync(
        string trackingNumber,
        IReadOnlyList<IncomingScan> scans,
        string uploadingCourierId,
        CancellationToken ct = default);
}

/// <summary>The value of a use case that succeeds with nothing to return.</summary>
public readonly record struct Done;
