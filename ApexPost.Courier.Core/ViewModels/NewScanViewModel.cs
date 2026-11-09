using System.Text.RegularExpressions;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Core.ViewModels;

/// <summary>
/// Records a scan. It is always saved on the device first, as Pending, whatever the network is doing. Then, if the
/// device is online, a sync starts in the background.
/// </summary>
public sealed partial class NewScanViewModel : ObservableObject
{
    private readonly IScanRepository _scans;
    private readonly ITokenStore _tokens;
    private readonly ScanSyncService _sync;
    private readonly IConnectivityMonitor _connectivity;
    private readonly TimeProvider _clock;
    private string _trackingNumber = string.Empty;
    private ScanType _scanType = ScanType.Pickup;
    private string _location = string.Empty;
    private string _notes = string.Empty;
    private string _signature = string.Empty;
    private string? _message;

    public NewScanViewModel(IScanRepository scans, ITokenStore tokens, ScanSyncService sync, IConnectivityMonitor connectivity, TimeProvider clock)
    {
        _scans = scans;
        _tokens = tokens;
        _sync = sync;
        _connectivity = connectivity;
        _clock = clock;
        SaveCommand = new AsyncCommand(SaveAsync);
    }

    public static IReadOnlyList<ScanType> ScanTypes { get; } = Enum.GetValues<ScanType>();

    public string TrackingNumber { get => _trackingNumber; set => Set(ref _trackingNumber, value); }

    public ScanType ScanType
    {
        get => _scanType;
        set
        {
            if (Set(ref _scanType, value))
            {
                Raise(nameof(NeedsSignature));
            }
        }
    }

    public bool NeedsSignature => ScanType == ScanType.Delivered;

    public string Location { get => _location; set => Set(ref _location, value); }

    public string Notes { get => _notes; set => Set(ref _notes, value); }

    public string SignatureName { get => _signature; set => Set(ref _signature, value); }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public AsyncCommand SaveCommand { get; }

    /// <summary>The last background sync this page started, for tests.</summary>
    internal Task? LastSync { get; private set; }

    [GeneratedRegex(@"^APX-\d{8}$", RegexOptions.ECMAScript)]
    private static partial Regex TrackingNumberFormat();

    public async Task SaveAsync()
    {
        var courierId = CourierIdentity.CourierIdFrom(await _tokens.GetAsync());
        if (courierId is null)
        {
            Message = "Sign in first.";
            return;
        }

        var tracking = TrackingNumber.Trim().ToUpperInvariant();
        if (!TrackingNumberFormat().IsMatch(tracking))
        {
            Message = "The tracking number looks like APX-12345678.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Location))
        {
            Message = "Enter where you are.";
            return;
        }

        if (NeedsSignature && string.IsNullOrWhiteSpace(SignatureName))
        {
            Message = "A delivery needs the name of the person who signed.";
            return;
        }

        ShipmentScan scan;
        try
        {
            scan = _scans.Create(ShipmentScan.CreatePending(
                tracking, ScanType, courierId, Location, _clock.GetUtcNow(), Notes, NeedsSignature ? SignatureName : null));
        }
        catch (ArgumentException exception)
        {
            Message = exception.Message;
            return;
        }

        Message = $"Saved on this device as Pending ({scan.ScanType} for {scan.TrackingNumber}).";
        TrackingNumber = string.Empty;
        Notes = string.Empty;
        SignatureName = string.Empty;
        if (_connectivity.IsOnline)
        {
            LastSync = _sync.SyncPendingAsync();
        }
    }
}
