# Prompt 1: unit tests for OutboxService

Sent to GitHub Copilot CLI 1.0.112 (`copilot -p`, Copilot Free plan, model chosen automatically, no tools allowed). The exact prompt:

````text
Write xUnit 2 unit tests that use Moq 4.20 for the C# class OutboxService shown below. Mock IScanRepository and IShipmentUploader; do not use a real database. Follow the Arrange-Act-Assert pattern with // Arrange, // Act and // Assert comments, and name every test Method_Scenario_ExpectedResult. Cover: a successful upload marks the scan Synced; a rejected upload marks it Failed with the error; an uploader that throws marks that scan Failed and the run continues; a TransientUploadException leaves the scans Pending and stops the run; a scan deleted while it uploads is skipped; cancellation. Use Setup with ReturnsAsync, argument matching with It.Is and It.IsAny, and Verify with Times. Scans are created with `public static ShipmentScan CreatePending(
        string trackingNumber,
        ScanType scanType,
        string courierId,
        string location,
        DateTimeOffset scannedAt,
        string? notes = null,
        string? recipientSignatureName = null)` (namespace ApexPost.Courier.Offline.Domain; ScanType has Pickup, InTransit, Delivered, Failed; the id is in the Id property). Reply with one C# file only, no explanation.

// ApexPost.Courier.Offline/Sync/OutboxService.cs
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// The outbox. Scans are saved locally as Pending first; this service reads the Pending ones, hands each to the
/// uploader and records the answer as Synced or Failed. In Part A the uploader is a fake. In Part D it is the real API call.
/// An uploader that cannot reach the server throws <see cref="TransientUploadException"/>: the scan stays Pending and
/// the run stops, since the next scans would fail the same way.
/// </summary>
/// <remarks>
/// Delivery is at least once: if the app stops after an upload but before the scan is marked Synced, the scan is
/// uploaded again on the next run. The scan id is therefore the idempotency key the server must honour.
/// </remarks>
public sealed class OutboxService(IScanRepository repository, IShipmentUploader uploader)
{
    public async Task<OutboxResult> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        var synced = new List<string>();
        var failures = new List<OutboxFailure>();
        var skipped = new List<string>();
        var deferred = new List<string>();

        foreach (var scan in repository.FindPending())
        {
            cancellationToken.ThrowIfCancellationRequested();

            UploadResult result;
            try
            {
                result = await uploader.UploadAsync(scan, cancellationToken).ConfigureAwait(false)
                    ?? UploadResult.Failure("The uploader returned no result.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TransientUploadException)
            {
                deferred.AddRange(repository.FindPending().Select(pending => pending.Id));
                break;
            }
            catch (Exception exception)
            {
                result = UploadResult.Failure(exception.Message);
            }

            // Only the status changes, so an edit made to the scan while it was uploading is kept.
            // False means the scan was deleted meanwhile, and it must not come back.
            var status = result.Succeeded ? SyncStatus.Synced : SyncStatus.Failed;
            if (!repository.UpdateSyncStatus(scan.Id, status))
            {
                skipped.Add(scan.Id);
            }
            else if (result.Succeeded)
            {
                synced.Add(scan.Id);
            }
            else
            {
                failures.Add(new OutboxFailure(scan.Id, result.Error ?? "The upload failed."));
            }
        }

        return new OutboxResult(synced, failures, skipped, deferred);
    }
}

// ApexPost.Courier.Offline/Sync/OutboxResult.cs
namespace ApexPost.Courier.Offline.Sync;

/// <summary>One scan the uploader could not accept, and why.</summary>
public sealed record OutboxFailure(string ScanId, string Error);

/// <summary>What one run of the outbox did.</summary>
/// <param name="SyncedIds">Scans that were uploaded and are now Synced.</param>
/// <param name="Failures">Scans that were not accepted and are now Failed.</param>
/// <param name="SkippedIds">Scans that were deleted while their upload was running, so there was nothing left to mark.</param>
/// <param name="DeferredIds">Scans left Pending because the server could not be reached. The next run tries them again.</param>
public sealed record OutboxResult(
    IReadOnlyList<string> SyncedIds,
    IReadOnlyList<OutboxFailure> Failures,
    IReadOnlyList<string> SkippedIds,
    IReadOnlyList<string>? DeferredIds = null)
{
    public int Deferred => DeferredIds?.Count ?? 0;

    public int Synced => SyncedIds.Count;

    public int Failed => Failures.Count;

    public int Skipped => SkippedIds.Count;
}

// ApexPost.Courier.Offline/Domain/IScanRepository.cs
namespace ApexPost.Courier.Offline.Domain;

/// <summary>
/// Where the courier's scans are kept on the device. The interface speaks only in domain types, so the rest of
/// the app never learns which database sits behind it.
/// </summary>
public interface IScanRepository
{
    /// <summary>Saves a new scan locally. The scan must be Pending and its id must not exist yet.</summary>
    ShipmentScan Create(ShipmentScan scan);

    /// <summary>Key-value lookup by document id. Returns null when there is no such scan.</summary>
    ShipmentScan? Get(string id);

    /// <summary>Replaces the stored scan that has the same id. Returns false when it no longer exists.</summary>
    bool Update(ShipmentScan scan);

    /// <summary>Changes only the sync status of a stored scan, so a concurrent edit to other fields is kept.</summary>
    bool UpdateSyncStatus(string id, SyncStatus status);

    /// <summary>Deletes a scan by id. Returns false when it does not exist.</summary>
    bool Delete(string id);

    /// <summary>Every scan, oldest first.</summary>
    IReadOnlyList<ShipmentScan> GetAll();

    /// <summary>All scans for one tracking number, ordered by scannedAtUtc.</summary>
    IReadOnlyList<ShipmentScan> FindByTrackingNumber(string trackingNumber);

    /// <summary>All scans whose syncStatus is Pending, oldest first.</summary>
    IReadOnlyList<ShipmentScan> FindPending();

    /// <summary>The number of scans of each scan type for one courier. Every type is present, with 0 when there are none.</summary>
    IReadOnlyDictionary<ScanType, int> CountByScanType(string courierId);

    /// <summary>How many scans are still waiting to be uploaded.</summary>
    int CountPending();
}

// ApexPost.Courier.Offline/Domain/IShipmentUploader.cs
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

// ApexPost.Courier.Offline/Sync/TransientUploadException.cs
namespace ApexPost.Courier.Offline.Sync;

/// <summary>
/// An uploader throws this when the upload could not happen for a reason that may pass, such as no network or a
/// server that is down. The outbox leaves the scan Pending to try again, instead of marking it Failed for good.
/// </summary>
public sealed class TransientUploadException(string message, Exception? innerException = null) : Exception(message, innerException);
````
