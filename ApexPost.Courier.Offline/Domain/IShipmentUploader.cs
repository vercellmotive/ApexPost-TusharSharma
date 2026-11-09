namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Sends one scan to the server. In Part A a fake implements this; Part D replaces it with the real API call.
/// The scan's id is the idempotency key: the server must treat a second upload of the same id as the same scan.
/// </summary>
public interface IShipmentUploader
{
    Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of one upload attempt.</summary>
public sealed record UploadResult(bool Succeeded, string? Error = null)
{
    public static UploadResult Success { get; } = new(true);

    public static UploadResult Failure(string error) => new(false, error);
}
