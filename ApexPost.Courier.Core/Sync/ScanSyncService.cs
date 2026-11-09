using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.Logging;
using DomainSyncStatus = ApexPost.Courier.Offline.Domain.SyncStatus;

namespace ApexPost.Courier.Core.Sync;

/// <summary>
/// Uploads Pending scans from the Part A store in batches of 20, oldest first, and marks each one Synced or Failed
/// from the server's answer. When the server cannot be reached the run stops and the rest stay Pending, to go out
/// on the next run. Only one run happens at a time. It also starts a run whenever the device comes back online.
/// </summary>
public sealed class ScanSyncService : IDisposable
{
    public const int BatchSize = 20;

    private readonly IScanRepository _scans;
    private readonly Func<ApiShipmentUploader> _uploaders;
    private readonly IConnectivityMonitor _connectivity;
    private readonly TimeProvider _clock;
    private readonly ILogger<ScanSyncService> _logger;
    private readonly SemaphoreSlim _oneRunAtATime = new(1, 1);

    public ScanSyncService(
        IScanRepository scans,
        Func<ApiShipmentUploader> uploaders,
        IConnectivityMonitor connectivity,
        TimeProvider clock,
        ILogger<ScanSyncService> logger)
    {
        _scans = scans;
        // A new uploader for each run: this service lives as long as the app, and a typed HttpClient should not.
        _uploaders = uploaders;
        _connectivity = connectivity;
        _clock = clock;
        _logger = logger;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    /// <summary>The last finished run, for the Sync Status screen.</summary>
    public SyncReport? LastReport { get; private set; }

    public event EventHandler<SyncReport>? Completed;

    /// <summary>The run the last reconnect started, so a caller can wait for it.</summary>
    public Task? BackgroundRun { get; private set; }

    public async Task<SyncReport> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!_connectivity.IsOnline)
        {
            return Finish(SyncReport.Skipped(_clock.GetUtcNow(), "The device is offline. Scans stay on the device as Pending."));
        }

        if (!await _oneRunAtATime.WaitAsync(0, cancellationToken))
        {
            return SyncReport.Skipped(_clock.GetUtcNow(), "A sync is already running.");
        }

        try
        {
            return Finish(await RunAsync(cancellationToken));
        }
        finally
        {
            _oneRunAtATime.Release();
        }
    }

    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _oneRunAtATime.Dispose();
    }

    private async Task<SyncReport> RunAsync(CancellationToken cancellationToken)
    {
        var uploader = _uploaders();
        var pending = _scans.FindPending();
        var synced = 0;
        var failures = new List<SyncFailure>();
        var handled = 0;
        var leftOut = 0;
        string? stopReason = null;

        foreach (var batch in pending.Chunk(BatchSize))
        {
            // The API takes one shipment's scans per request, so a batch of 20 is split by tracking number.
            foreach (var shipment in batch.GroupBy(scan => scan.TrackingNumber))
            {
                var scans = shipment.ToList();
                var result = await uploader.UploadBatchAsync(shipment.Key, scans, cancellationToken);
                if (result.Succeeded)
                {
                    var rejected = (result.Value.Rejections ?? []).ToDictionary(rejection => rejection.ScanId, rejection => rejection.Reason ?? "Rejected by the server.");
                    foreach (var scan in scans)
                    {
                        if (rejected.TryGetValue(scan.Id, out var reason))
                        {
                            MarkFailed(scan, reason, failures);
                        }
                        else if (WasChangedSince(scan))
                        {
                            // Edited or deleted while it was uploading: what the server has is not what the device
                            // has now, so it is not marked Synced. An edited scan stays Pending and goes up again.
                            leftOut++;
                        }
                        else
                        {
                            // Accepted now, or a duplicate the server already had from an earlier attempt.
                            _scans.UpdateSyncStatus(scan.Id, DomainSyncStatus.Synced);
                            synced++;
                        }
                    }
                }
                else if (result.IsTransient || result.Failure == ApiFailureKind.Unauthorized)
                {
                    // The scans are fine; the server or the sign-in is not. Keep them Pending and stop this run.
                    stopReason = result.Message;
                    _logger.LogWarning("Sync stopped: {Reason}", result.Message);
                    return Report(synced, failures, pending.Count - handled + leftOut, stopReason);
                }
                else
                {
                    // 400, 403 or 404: these scans will be refused every time, so they are Failed, with the reason.
                    foreach (var scan in scans)
                    {
                        MarkFailed(scan, result.Message ?? "Refused by the server.", failures);
                    }
                }

                handled += scans.Count;
            }
        }

        return Report(synced, failures, leftOut, stopReason);
    }

    /// <summary>
    /// Moves every Failed scan back to Pending and uploads again. A scan fails when the server refuses it, for
    /// example because a back-office user had not created the shipment yet, and the courier can send it again.
    /// </summary>
    public async Task<SyncReport> RetryFailedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var scan in _scans.GetAll().Where(scan => scan.SyncStatus == DomainSyncStatus.Failed))
        {
            _scans.UpdateSyncStatus(scan.Id, DomainSyncStatus.Pending);
        }

        return await SyncPendingAsync(cancellationToken);
    }

    private bool WasChangedSince(ShipmentScan uploaded) =>
        _scans.Get(uploaded.Id) is not { } current || current != uploaded;

    private void MarkFailed(ShipmentScan scan, string reason, List<SyncFailure> failures)
    {
        _scans.UpdateSyncStatus(scan.Id, DomainSyncStatus.Failed);
        failures.Add(new SyncFailure(scan.Id, scan.TrackingNumber, reason));
    }

    private SyncReport Report(int synced, List<SyncFailure> failures, int leftPending, string? stopReason) =>
        new(_clock.GetUtcNow(), synced, failures.Count, leftPending, failures, stopReason);

    private SyncReport Finish(SyncReport report)
    {
        LastReport = report;
        _logger.LogInformation(
            "Sync finished: {Synced} synced, {Failed} failed, {Pending} still pending.", report.Synced, report.Failed, report.LeftPending);
        Completed?.Invoke(this, report);
        return report;
    }

    private void OnConnectivityChanged(object? sender, bool online)
    {
        if (online)
        {
            _logger.LogInformation("Back online: uploading Pending scans.");
            BackgroundRun = Task.Run(async () =>
            {
                try
                {
                    await SyncPendingAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Sync after reconnecting failed.");
                }
            });
        }
    }
}
