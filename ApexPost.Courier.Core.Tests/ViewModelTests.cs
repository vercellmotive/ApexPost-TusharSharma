using System.Net;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;
using ApexPost.Courier.Offline.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Core.Tests;

public sealed class ViewModelTests : IDisposable
{
    private readonly ClientHarness _harness = new();
    private readonly TempCourierDatabase _store = new();
    private readonly FakeConnectivity _connectivity = new(online: false);
    private readonly ScanSyncService _sync;

    public ViewModelTests() =>
        _sync = new ScanSyncService(_store.Repository, () => new ApiShipmentUploader(_harness.Client), _connectivity, TimeProvider.System, NullLogger<ScanSyncService>.Instance);

    public void Dispose()
    {
        _sync.Dispose();
        _store.Dispose();
        _harness.Dispose();
    }

    private NewScanViewModel NewScan() => new(_store.Repository, _harness.Tokens, _sync, _connectivity, TimeProvider.System);

    [Fact]
    public async Task A_scan_made_offline_is_saved_on_the_device_as_pending_under_the_token_courier()
    {
        var page = NewScan();
        page.TrackingNumber = "apx-10000001";
        page.Location = "Sydney depot";

        await page.SaveAsync();

        var saved = Assert.Single(_store.Repository.FindPending());
        Assert.Equal("APX-10000001", saved.TrackingNumber);
        Assert.Equal("CR-1001", saved.CourierId);
        Assert.Equal(SyncStatus.Pending, saved.SyncStatus);
        Assert.StartsWith("Saved on this device as Pending", page.Message);
        Assert.Equal(0, _harness.Server.Calls);
    }

    [Fact]
    public async Task A_delivery_needs_a_signature_and_keeps_it()
    {
        var page = NewScan();
        page.TrackingNumber = "APX-10000001";
        page.Location = "Front door";
        page.ScanType = ScanType.Delivered;

        await page.SaveAsync();
        Assert.True(page.NeedsSignature);
        Assert.Empty(_store.Repository.GetAll());

        page.SignatureName = "Mia Fictional";
        await page.SaveAsync();
        Assert.Equal("Mia Fictional", Assert.Single(_store.Repository.GetAll()).RecipientSignatureName);
    }

    [Theory]
    [InlineData("APX-1", "Depot", "looks like APX-12345678")]
    [InlineData("APX-10000001", " ", "Enter where you are")]
    public async Task Bad_input_is_explained_and_nothing_is_saved(string tracking, string location, string message)
    {
        var page = NewScan();
        page.TrackingNumber = tracking;
        page.Location = location;

        await page.SaveAsync();

        Assert.Contains(message, page.Message);
        Assert.Empty(_store.Repository.GetAll());
    }

    [Fact]
    public async Task Without_a_sign_in_nothing_is_saved()
    {
        await _harness.Tokens.ClearAsync();
        var page = NewScan();
        page.TrackingNumber = "APX-10000001";
        page.Location = "Depot";

        await page.SaveAsync();

        Assert.Equal("Sign in first.", page.Message);
    }

    [Fact]
    public async Task Online_a_new_scan_is_uploaded_straight_after_it_is_saved()
    {
        _connectivity.Set(true);
        await (_sync.BackgroundRun ?? Task.CompletedTask);
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        var page = NewScan();
        page.TrackingNumber = "APX-10000001";
        page.Location = "Depot";

        await page.SaveAsync();
        await page.LastSync!;

        Assert.Equal(SyncStatus.Synced, Assert.Single(_store.Repository.GetAll()).SyncStatus);
    }

    [Fact]
    public async Task The_pending_screen_follows_the_change_listener_count()
    {
        using var pending = new PendingScansViewModel(_store.Database, _store.Repository, new InlineDispatcher());
        var page = NewScan();
        page.TrackingNumber = "APX-10000001";
        page.Location = "Depot";

        await page.SaveAsync();
        await Wait.UntilAsync(() => pending.PendingCount == 1);
        Assert.Equal("Pending scans: 1", pending.Title);
        Assert.Equal("APX-10000001", Assert.Single(pending.Scans).TrackingNumber);

        _store.Repository.UpdateSyncStatus(_store.Repository.FindPending()[0].Id, SyncStatus.Synced);
        await Wait.UntilAsync(() => pending.PendingCount == 0);
        Assert.Empty(pending.Scans);
    }

    [Fact]
    public async Task Sync_status_shows_the_last_run_failures_and_the_offline_banner()
    {
        var circuit = new CircuitStateMonitor();
        using var status = new SyncStatusViewModel(_sync, circuit, _connectivity, new InlineDispatcher());
        Assert.True(status.ShowOfflineBanner);
        Assert.Equal("Server unavailable – working offline", status.OfflineBanner);

        _connectivity.Set(true);
        await (_sync.BackgroundRun ?? Task.CompletedTask);
        Assert.False(status.ShowOfflineBanner);

        _store.Repository.Create(ShipmentScan.CreatePending("APX-99999999", ScanType.Pickup, "CR-1001", "Depot", DateTimeOffset.UtcNow));
        _harness.Server.Then(HttpStatusCode.NotFound, """{"status":404,"detail":"No shipment has tracking number APX-99999999."}""");
        await status.SyncNowCommand.ExecuteAsync();

        Assert.Contains("0 synced, 1 failed", status.LastResultText);
        Assert.Equal("APX-99999999: No shipment has tracking number APX-99999999.", Assert.Single(status.Failures));
        Assert.NotEqual("Not synced yet", status.LastSyncText);

        circuit.Set(CircuitState.Open);
        Assert.True(status.ShowOfflineBanner);
        Assert.StartsWith("Open", status.CircuitText);
    }

#if DEBUG
    [Fact]
    public async Task Login_stores_a_development_token_in_the_token_store_and_opens_the_app()
    {
        await _harness.Tokens.ClearAsync();
        var navigator = new RecordingNavigator();
        var login = new LoginViewModel(_harness.Tokens, _harness.Client, navigator) { CourierId = "CR-1001" };
        _harness.Server.Then(HttpStatusCode.OK, $$"""{"accessToken":"{{ClientHarness.Token}}","tokenType":"Bearer","expiresAtUtc":"2026-11-09T03:00:00Z"}""");

        await login.RequestTokenAsync();

        Assert.Equal(ClientHarness.Token, _harness.Tokens.Token);
        Assert.Equal(["main"], navigator.Visits);
        Assert.Equal("/dev/token", _harness.Server.Received.Single().Path);
    }
#endif

    [Fact]
    public async Task A_pasted_token_without_a_courier_id_is_refused()
    {
        await _harness.Tokens.ClearAsync();
        var navigator = new RecordingNavigator();
        var login = new LoginViewModel(_harness.Tokens, _harness.Client, navigator) { PastedToken = "not-a-token" };

        await login.UsePastedTokenAsync();

        Assert.Null(_harness.Tokens.Token);
        Assert.Empty(navigator.Visits);
        Assert.Contains("courierId", login.Message);
    }
}
