using System.Globalization;
using System.Text;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Offline.Sync;
using Couchbase.Lite;

namespace ApexPost.Courier.Offline.Harness;

/// <summary>
/// The console menu. It reads lines from a <see cref="TextReader"/> and writes to a <see cref="TextWriter"/>, so a
/// test can drive it with a script. Nothing here touches the network.
/// </summary>
public sealed class HarnessApp
{
    private readonly CourierDatabase _database;
    private readonly IScanRepository _repository;
    private readonly OutboxService _outbox;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TimeProvider _time;
    private readonly bool _echoInput;
    private readonly TimeSpan _settleDelay;
    private bool _inputEnded;

    /// <param name="echoInput">Repeat each line read, for input that comes from a file or a pipe rather than a keyboard.</param>
    /// <param name="settleDelay">
    /// A short pause after each action so the change listener's lines appear before the next menu. Notifications
    /// arrive a few milliseconds after a save, on another thread. Tests leave this at zero.
    /// </param>
    public HarnessApp(
        CourierDatabase database,
        IScanRepository repository,
        OutboxService outbox,
        TextReader input,
        TextWriter output,
        bool echoInput = false,
        TimeProvider? time = null,
        TimeSpan settleDelay = default)
    {
        _database = database;
        _repository = repository;
        _outbox = outbox;
        _input = input;
        _output = TextWriter.Synchronized(output);
        _echoInput = echoInput;
        _settleDelay = settleDelay;
        _time = time ?? TimeProvider.System;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        // The collection change listener raises this after every change, so the pending count stays live.
        _database.ScansChanged += OnScansChanged;
        try
        {
            while (!_inputEnded && !cancellationToken.IsCancellationRequested)
            {
                _output.Write(MenuText());
                var choice = Ask("Choose an option (1-6): ");
                if (choice is null)
                {
                    break;
                }

                try
                {
                    if (!await RunChoiceAsync(choice.Trim(), cancellationToken))
                    {
                        return 0;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException or CouchbaseLiteException)
                {
                    _output.WriteLine($"Could not do that: {exception.Message}");
                }

                if (_settleDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_settleDelay, cancellationToken);
                }
            }

            return 0;
        }
        finally
        {
            _database.ScansChanged -= OnScansChanged;
        }
    }

    private async Task<bool> RunChoiceAsync(string choice, CancellationToken cancellationToken)
    {
        switch (choice)
        {
            case "1":
                AddScan();
                break;
            case "2":
                FindByTrackingNumber();
                break;
            case "3":
                ListPending();
                break;
            case "4":
                await SimulateSyncAsync(cancellationToken);
                break;
            case "5":
                DeleteScan();
                break;
            case "6":
                _output.WriteLine("Closing the database. Goodbye.");
                return false;
            default:
                _output.WriteLine("Please choose a number from 1 to 6.");
                break;
        }

        return true;
    }

    private void AddScan()
    {
        var trackingNumber = Ask("Tracking number: ");
        var scanTypeText = trackingNumber is null ? null : Ask("Scan type (1=Pickup, 2=InTransit, 3=Delivered, 4=Failed): ");
        var courierId = scanTypeText is null ? null : Ask("Courier ID: ");
        var location = courierId is null ? null : Ask("Location: ");
        var notes = location is null ? null : Ask("Notes (optional): ");
        if (notes is null)
        {
            return;
        }

        if (!TryParseScanType(scanTypeText!, out var scanType))
        {
            _output.WriteLine("That is not a scan type. Use 1 to 4, or Pickup, InTransit, Delivered, Failed.");
            return;
        }

        // Saved on the device first, as Pending. Nothing is sent anywhere.
        if (!System.Text.RegularExpressions.Regex.IsMatch(trackingNumber!.Trim(), @"^APX-\d{8}$", System.Text.RegularExpressions.RegexOptions.ECMAScript))
        {
            _output.WriteLine("Tracking numbers look like APX-12345678, the format the API accepts. Nothing was saved.");
            return;
        }

        var scan = ShipmentScan.CreatePending(trackingNumber!, scanType, courierId!, location!, _time.GetUtcNow(), notes);
        _repository.Create(scan);
        _output.WriteLine($"Saved {scan.Id} on this device as Pending.");

        var counts = _repository.CountByScanType(scan.CourierId);
        _output.WriteLine($"Courier {scan.CourierId} has {counts.Values.Sum()} scan(s): {string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"))}.");
    }

    private void FindByTrackingNumber()
    {
        var trackingNumber = Ask("Tracking number: ");
        if (trackingNumber is null)
        {
            return;
        }

        var scans = _repository.FindByTrackingNumber(trackingNumber.Trim());
        _output.WriteLine(scans.Count == 0
            ? $"No scans found for {trackingNumber.Trim()}."
            : $"{scans.Count} scan(s) for {trackingNumber.Trim()}, oldest first:");
        foreach (var scan in scans)
        {
            _output.WriteLine(Describe(scan));
        }
    }

    private void ListPending()
    {
        var scans = _repository.FindPending();
        _output.WriteLine(scans.Count == 0 ? "No scans are waiting to sync." : $"{scans.Count} scan(s) waiting to sync, oldest first:");
        foreach (var scan in scans)
        {
            _output.WriteLine(Describe(scan));
        }
    }

    private async Task SimulateSyncAsync(CancellationToken cancellationToken)
    {
        if (_repository.CountPending() == 0)
        {
            _output.WriteLine("Nothing is waiting to sync.");
            return;
        }

        _output.WriteLine("Sending pending scans to the fake server...");
        var result = await _outbox.SyncPendingAsync(cancellationToken);
        foreach (var id in result.SyncedIds)
        {
            _output.WriteLine($"  Synced   {id}");
        }

        foreach (var failure in result.Failures)
        {
            _output.WriteLine($"  Failed   {failure.ScanId}  ({failure.Error})");
        }

        foreach (var id in result.SkippedIds)
        {
            _output.WriteLine($"  Skipped  {id}  (deleted while it was uploading)");
        }

        _output.WriteLine($"Done: {result.Synced} synced, {result.Failed} failed, {result.Skipped} skipped.");
    }

    private void DeleteScan()
    {
        var scans = _repository.GetAll();
        if (scans.Count == 0)
        {
            _output.WriteLine("There are no scans to delete.");
            return;
        }

        _output.WriteLine($"{scans.Count} scan(s) on this device:");
        foreach (var scan in scans)
        {
            _output.WriteLine(Describe(scan));
        }

        var id = Ask("Scan ID to delete: ");
        if (id is null)
        {
            return;
        }

        id = id.Trim();
        if (!ShipmentScan.IsValidId(id))
        {
            _output.WriteLine($"That is not a scan id. It looks like {ShipmentScan.IdPrefix}{{guid}}.");
            return;
        }

        _output.WriteLine(_repository.Delete(id) ? $"Deleted {id}." : $"No scan has the id {id}.");
    }

    private void OnScansChanged(object? sender, ScansChangedEventArgs e) =>
        _output.WriteLine($"  [pending] {e.PendingCount} scan(s) waiting to sync");

    private string MenuText()
    {
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine("=== ApexPost courier offline harness ===");
        text.AppendLine($"Database : {_database.Name}  ({_database.Path})");
        text.AppendLine("Network  : not used, everything works offline");
        text.AppendLine($"Tip      : a note containing {FakeShipmentUploader.FailureMarker} makes the fake server reject that scan");
        text.AppendLine($"Pending  : {_repository.CountPending()} scan(s) waiting to sync");
        text.AppendLine("  1) Add Scan");
        text.AppendLine("  2) Find by Tracking Number");
        text.AppendLine("  3) List Pending");
        text.AppendLine("  4) Simulate Sync");
        text.AppendLine("  5) Delete Scan");
        text.AppendLine("  6) Exit");
        return text.ToString();
    }

    private string? Ask(string prompt)
    {
        _output.Write(prompt);
        var line = _input.ReadLine();
        if (line is null)
        {
            _inputEnded = true;
            _output.WriteLine();
        }
        else if (_echoInput)
        {
            _output.WriteLine(line);
        }

        return line;
    }

    private static bool TryParseScanType(string text, out ScanType scanType)
    {
        var cleaned = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var all = Enum.GetValues<ScanType>();
        if (int.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= all.Length)
        {
            scanType = all[number - 1];
            return true;
        }

        foreach (var candidate in all)
        {
            if (string.Equals(candidate.ToString(), cleaned, StringComparison.OrdinalIgnoreCase))
            {
                scanType = candidate;
                return true;
            }
        }

        scanType = default;
        return false;
    }

    private static string Describe(ShipmentScan scan)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"  {scan.ScannedAtUtc:yyyy-MM-dd HH:mm:ss} UTC  {scan.TrackingNumber,-14} {scan.ScanType,-9} {scan.CourierId,-8} {scan.Location,-16} [{scan.SyncStatus}]  {scan.Id}");
        return scan.Notes is null ? line : $"{line}  note: {scan.Notes}";
    }
}
