using System.Collections.ObjectModel;
using System.Globalization;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;

namespace ApexPost.Courier.Core.ViewModels;

/// <summary>The Sync Status screen: last sync time, circuit state and failures, plus the offline banner.</summary>
public sealed class SyncStatusViewModel : ObservableObject, IDisposable
{
    private readonly ScanSyncService _sync;
    private readonly CircuitStateMonitor _circuit;
    private readonly IConnectivityMonitor _connectivity;
    private readonly IUiDispatcher _dispatcher;

    public SyncStatusViewModel(ScanSyncService sync, CircuitStateMonitor circuit, IConnectivityMonitor connectivity, IUiDispatcher dispatcher)
    {
        _sync = sync;
        _circuit = circuit;
        _connectivity = connectivity;
        _dispatcher = dispatcher;
        SyncNowCommand = new AsyncCommand(async () => await _sync.SyncPendingAsync());
        _sync.Completed += OnCompleted;
        _circuit.Changed += OnCircuitChanged;
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
        Show(_sync.LastReport);
    }

    public AsyncCommand SyncNowCommand { get; }

    /// <summary>Shown while the circuit is open or the device is offline. Scans are still saved on the device.</summary>
    public bool ShowOfflineBanner => _circuit.IsOpen || !_connectivity.IsOnline;

    public string OfflineBanner => ApexApiClient.OfflineMessage;

    public string CircuitText => _circuit.State switch
    {
        CircuitState.Open => "Open: calls to the server are paused",
        CircuitState.HalfOpen => "Half-open: testing the server",
        _ => "Closed: the server is answering",
    };

    public string LastSyncText { get; private set; } = "Not synced yet";

    public string? LastResultText { get; private set; }

    public ObservableCollection<string> Failures { get; } = [];

    public void Dispose()
    {
        _sync.Completed -= OnCompleted;
        _circuit.Changed -= OnCircuitChanged;
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnCompleted(object? sender, SyncReport report) => _dispatcher.Post(() => Show(report));

    private void OnCircuitChanged(object? sender, CircuitState state) => _dispatcher.Post(RaiseState);

    private void OnConnectivityChanged(object? sender, bool online) => _dispatcher.Post(RaiseState);

    private void RaiseState()
    {
        Raise(nameof(CircuitText));
        Raise(nameof(ShowOfflineBanner));
    }

    private void Show(SyncReport? report)
    {
        if (report is not null)
        {
            LastSyncText = report.FinishedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            LastResultText = report.StopReason is null
                ? $"{report.Synced} synced, {report.Failed} failed, {report.LeftPending} still pending"
                : $"{report.Synced} synced, {report.Failed} failed, {report.LeftPending} still pending. {report.StopReason}";
            foreach (var failure in report.Failures)
            {
                Failures.Insert(0, $"{failure.TrackingNumber}: {failure.Reason}");
            }

            Raise(nameof(LastSyncText));
            Raise(nameof(LastResultText));
        }

        RaiseState();
    }
}
