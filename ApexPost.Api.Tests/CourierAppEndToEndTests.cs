using ApexPost.Api.Security;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Tracking.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OfflineScanType = ApexPost.Courier.Offline.Domain.ScanType;
using OfflineSyncStatus = ApexPost.Courier.Offline.Domain.SyncStatus;
using OfflineScan = ApexPost.Courier.Offline.Domain.ShipmentScan;

namespace ApexPost.Api.Tests;

/// <summary>
/// The Part D flow end to end, with no fakes between the app logic and the API: the courier app's view models,
/// typed client, auth handler, resilience pipeline and Couchbase Lite store, talking to the real API pipeline.
/// </summary>
public sealed class CourierAppEndToEndTests : IDisposable
{
    private readonly ApiFactory _api = new("Development");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "apexpost-e2e", Guid.NewGuid().ToString("N"));
    private readonly CourierDatabase _database;
    private readonly CouchbaseScanRepository _scans;
    private readonly ServiceProvider _app;
    private readonly Connectivity _connectivity = new();
    private readonly ScanSyncService _sync;
    private readonly TokenStore _tokens = new();

    public CourierAppEndToEndTests()
    {
        _api.Store.Seed(Make.Shipment("APX-10000001"));
        _database = new CourierDatabase(new CourierDatabaseOptions { Directory = _folder });
        _scans = new CouchbaseScanRepository(_database);

        // The same registration as MauiProgram, with the in-memory API server as the network.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITokenStore>(_tokens);
        services.AddSingleton<SessionEvents>();
        services.AddTransient<AuthHeaderHandler>();
        services.AddHttpClient<ApexApiClient>(client => client.BaseAddress = _api.Server.BaseAddress)
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .AddApexResilience(new ApexResilienceOptions { RetryBaseDelay = TimeSpan.FromMilliseconds(1) })
            .ConfigurePrimaryHttpMessageHandler(() => _api.Server.CreateHandler());
        _app = services.BuildServiceProvider();
        _sync = new ScanSyncService(_scans, () => new ApiShipmentUploader(Client), _connectivity, _api.Clock, NullLogger<ScanSyncService>.Instance);
    }

    private Task SignInAsync(string courierId) => _tokens.SetAsync(ApiFactory.Token([ApexPostRoles.Courier], courierId));

    private ApexApiClient Client => _app.GetRequiredService<ApexApiClient>();

    public void Dispose()
    {
        _sync.Dispose();
        _app.Dispose();
        _database.Dispose();
        _api.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Scans_saved_offline_upload_by_themselves_on_reconnect_and_only_once()
    {
        // Login: a token for courier CR-1001, pasted on the Login page and kept in the token store.
        var login = new LoginViewModel(_tokens, Client, new Navigator()) { PastedToken = ApiFactory.Token([ApexPostRoles.Courier], Make.CourierId) };
        await login.UsePastedTokenAsync();
        Assert.Equal(Make.CourierId, CourierIdentity.CourierIdFrom(_tokens.Token));

        // Airplane mode: 25 scans are saved on the device as Pending, and nothing reaches the server.
        _connectivity.Set(false);
        var newScan = new NewScanViewModel(_scans, _tokens, _sync, _connectivity, _api.Clock);
        for (var index = 0; index < 25; index++)
        {
            newScan.TrackingNumber = "APX-10000001";
            newScan.ScanType = index == 24 ? OfflineScanType.Delivered : OfflineScanType.InTransit;
            newScan.Location = $"Stop {index + 1}";
            newScan.SignatureName = "Mia Fictional";
            _api.Clock.Now = _api.Clock.Now.AddMinutes(1);
            await newScan.SaveAsync();
        }

        Assert.Equal(25, _scans.FindPending().Count);
        Assert.Empty(_api.Store.AllScans);

        // Back online: the connectivity event starts the upload, in batches of 20.
        _connectivity.Set(true);
        await _sync.BackgroundRun!;

        Assert.Equal(25, _sync.LastReport!.Synced);
        Assert.Empty(_scans.FindPending());
        Assert.All(_scans.GetAll(), scan => Assert.Equal(OfflineSyncStatus.Synced, scan.SyncStatus));
        Assert.Equal(25, _api.Store.AllScans.Count);
        Assert.Equal("Mia Fictional", _api.Store.AllScans.Single(scan => scan.ScanType == ScanType.Delivered).RecipientSignatureName);

        // A reply lost on the way back: the device still thinks one scan is Pending and sends it again.
        var resent = _scans.GetAll()[0];
        _scans.UpdateSyncStatus(resent.Id, OfflineSyncStatus.Pending);
        var again = await _sync.SyncPendingAsync();

        Assert.Equal(1, again.Synced);
        Assert.Equal(25, _api.Store.AllScans.Count);

        // The shipment now shows the latest scan through the v2 contract the app reads.
        var shipment = await Client.GetShipmentAsync("APX-10000001");
        Assert.True(shipment.Succeeded);
        Assert.Equal(ScanKind.Delivered, shipment.Value.LatestScan?.ScanType);
    }

    [Fact]
    public async Task A_scan_the_server_refuses_is_marked_failed_with_the_reason()
    {
        await SignInAsync(Make.CourierId);
        var unknown = _scans.Create(OfflineScan.CreatePending("APX-99999999", OfflineScanType.Pickup, Make.CourierId, "Depot", _api.Clock.GetUtcNow()));

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(OfflineSyncStatus.Failed, _scans.Get(unknown.Id)!.SyncStatus);
        Assert.Equal("No shipment has tracking number APX-99999999.", Assert.Single(report.Failures).Reason);
    }

    [Fact]
    public async Task Another_couriers_token_cannot_upload_this_couriers_scans()
    {
        await SignInAsync("CR-2002");
        var scan = _scans.Create(OfflineScan.CreatePending("APX-10000001", OfflineScanType.Pickup, Make.CourierId, "Depot", _api.Clock.GetUtcNow()));

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(OfflineSyncStatus.Failed, _scans.Get(scan.Id)!.SyncStatus);
        Assert.Contains("own courier id", Assert.Single(report.Failures).Reason);
        Assert.Empty(_api.Store.AllScans);
    }

    private sealed class TokenStore : ITokenStore
    {
        public string? Token { get; private set; }

        public Task<string?> GetAsync() => Task.FromResult(Token);

        public Task SetAsync(string token)
        {
            Token = token;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Token = null;
            return Task.CompletedTask;
        }
    }

    private sealed class Connectivity : IConnectivityMonitor
    {
        public bool IsOnline { get; private set; } = true;

        public event EventHandler<bool>? ConnectivityChanged;

        public void Set(bool online)
        {
            IsOnline = online;
            ConnectivityChanged?.Invoke(this, online);
        }
    }

    private sealed class Navigator : INavigator
    {
        public Task ShowLoginAsync() => Task.CompletedTask;

        public Task ShowMainAsync() => Task.CompletedTask;
    }
}
