using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;

namespace ApexPost.Courier.Core.Sync;

/// <summary>
/// The real uploader that replaces Part A's FakeShipmentUploader. It sends scans through <see cref="ApexApiClient"/>
/// and works in batches for <see cref="ScanSyncService"/>, and one scan at a time for the Part A OutboxService.
/// A failure that may pass leaves the scan Pending: the outbox gets a <see cref="TransientUploadException"/>.
/// </summary>
public sealed class ApiShipmentUploader(ApexApiClient api) : IShipmentUploader
{
    public async Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default)
    {
        var result = await UploadBatchAsync(scan.TrackingNumber, [scan], cancellationToken);
        if (!result.Succeeded)
        {
            return result.IsTransient
                ? throw new TransientUploadException(result.Message ?? "The server could not be reached.")
                : UploadResult.Failure(result.Message ?? "The server refused the scan.");
        }

        return result.Value.Rejections?.FirstOrDefault() is { } rejection
            ? UploadResult.Failure(rejection.Reason ?? "The server rejected the scan.")
            : UploadResult.Success;
    }

    public Task<ApiResult<ScanUploadResponseDto>> UploadBatchAsync(
        string trackingNumber,
        IReadOnlyList<ShipmentScan> scans,
        CancellationToken cancellationToken = default) =>
        api.UploadScansAsync(trackingNumber, scans.Select(ToDto).ToList(), cancellationToken);

    internal static ScanUploadItemDto ToDto(ShipmentScan scan) => new(
        scan.Id,
        scan.ScanType switch
        {
            ScanType.Pickup => ScanKind.Pickup,
            ScanType.InTransit => ScanKind.InTransit,
            ScanType.Delivered => ScanKind.Delivered,
            ScanType.Failed => ScanKind.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(scan), scan.ScanType, "Unknown scan type."),
        },
        scan.CourierId,
        scan.Location,
        scan.ScannedAtUtc,
        scan.Notes,
        scan.RecipientSignatureName);
}
