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
