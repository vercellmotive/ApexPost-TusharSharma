using ApexPost.Tracking.Domain;

namespace ApexPost.Tracking.Application;

/// <summary>
/// Storage for shipments and their scans. Changes to shipments are staged and written by <see cref="SaveChangesAsync"/>;
/// it reports a duplicate tracking number as the Week 3 <c>DuplicateValueException</c> and a lost update as
/// <c>ConcurrencyConflictException</c>.
/// </summary>
public interface IShipmentStore
{
    Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default);

    /// <summary>Reads a shipment and its latest scan, without tracking it.</summary>
    Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default);

    /// <summary>Loads a shipment to change it.</summary>
    Task<Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default);

    Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default);

    void Add(Shipment shipment);

    void Remove(Shipment shipment);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>The shipment's scans, oldest first.</summary>
    Task<IReadOnlyList<TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default);

    /// <summary>For each of these scan ids that is already stored, the id of the shipment it belongs to.</summary>
    Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default);

    /// <summary>
    /// Inserts the scans at once. A scan id another request stored in the meantime is skipped rather than failing the
    /// rest. Returns the ids this call stored.
    /// </summary>
    Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<TrackingScan> scans, CancellationToken ct = default);
}
