using System.Collections.ObjectModel;
using System.Globalization;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;

namespace ApexPost.Courier.Core.ViewModels;

/// <summary>One row of the Pending Scans list, ready to show.</summary>
public sealed record PendingScanItem(string ScanId, string TrackingNumber, string Detail)
{
    public static PendingScanItem From(ShipmentScan scan) => new(
        scan.Id,
        scan.TrackingNumber,
        $"{scan.ScanType} at {scan.Location}, {scan.ScannedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}");
}

/// <summary>
/// The Pending Scans screen. The Part A collection change listener raises ScansChanged with the live pending
/// count on every write, so the count and the list stay current while sync runs in the background.
/// </summary>
public sealed class PendingScansViewModel : ObservableObject, IDisposable
{
    private readonly CourierDatabase _database;
    private readonly IScanRepository _scans;
    private readonly IUiDispatcher _dispatcher;
    private int _pendingCount;

    public PendingScansViewModel(CourierDatabase database, IScanRepository scans, IUiDispatcher dispatcher)
    {
        _database = database;
        _scans = scans;
        _dispatcher = dispatcher;
        _pendingCount = database.PendingCount;
        _database.ScansChanged += OnScansChanged;
        Refresh();
    }

    public int PendingCount { get => _pendingCount; private set => Set(ref _pendingCount, value); }

    public string Title => $"Pending scans: {PendingCount}";

    public ObservableCollection<PendingScanItem> Scans { get; } = [];

    public void Refresh()
    {
        Scans.Clear();
        foreach (var scan in _scans.FindPending())
        {
            Scans.Add(PendingScanItem.From(scan));
        }

        PendingCount = _database.PendingCount;
        Raise(nameof(Title));
    }

    public void Dispose() => _database.ScansChanged -= OnScansChanged;

    private void OnScansChanged(object? sender, ScansChangedEventArgs e) => _dispatcher.Post(Refresh);
}
