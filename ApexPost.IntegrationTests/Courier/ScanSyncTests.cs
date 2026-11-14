using System.Net;
using System.Text.Json;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using ApexPost.Testing.Courier;

namespace ApexPost.IntegrationTests.Courier;

public sealed class ScanSyncTests : IDisposable
{
    private readonly ClientHarness _harness = new();
    private readonly TempCourierDatabase _store = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly ScanSyncService _sync;

    public ScanSyncTests() =>
        _sync = new ScanSyncService(_store.Repository, () => new ApiShipmentUploader(_harness.Client), _connectivity, TimeProvider.System, NullLogger<ScanSyncService>.Instance);

    public void Dispose()
    {
        _sync.Dispose();
        _store.Dispose();
        _harness.Dispose();
    }

    private ShipmentScan Save(string tracking = "APX-10000001", int minutesAgo = 10, ScanType type = ScanType.Pickup) =>
        _store.Repository.Create(ShipmentScan.CreatePending(tracking, type, "CR-1001", "Depot", Scans.ScannedAt.AddMinutes(-minutesAgo)));

    // The fake server accepts everything except the scan ids in rejectIds.
    private void AcceptAll(params string[] rejectIds) => _harness.Server.Fallback = async request =>
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var ids = body.RootElement.GetProperty("scans").EnumerateArray().Select(scan => scan.GetProperty("scanId").GetString()!).ToList();
        var rejected = ids.Where(rejectIds.Contains).Select(id => $$"""{"scanId":"{{id}}","reason":"The scan time is before the shipment was created."}""");
        return ScriptedServer.Respond(HttpStatusCode.OK, $$"""{"accepted":{{ids.Count - rejected.Count()}},"duplicatesSkipped":0,"rejected":{{rejected.Count()}},"rejections":[{{string.Join(",", rejected)}}]}""");
    };

    [Fact]
    public async Task SyncNow_ManyPendingScans_UploadsBatchesOf20PerShipment()
    {
        // Arrange
        for (var index = 0; index < 45; index++)
        {
            Save(index % 3 == 0 ? "APX-10000002" : "APX-10000001", minutesAgo: 100 - index);
        }

        AcceptAll();

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(45, report.Synced);
        Assert.Empty(_store.Repository.FindPending());

        // Act
        // The change listener updates the live count on its own thread.
        await Wait.UntilAsync(() => _store.Database.PendingCount == 0);

        // Assert
        var sizes = _harness.Server.Received.Select(request => JsonDocument.Parse(request.Body!).RootElement.GetProperty("scans").GetArrayLength()).ToList();

        Assert.All(sizes, size => Assert.InRange(size, 1, ScanSyncService.BatchSize));
        Assert.Equal(6, sizes.Count);
        Assert.Equal(45, sizes.Sum());
    }

    [Fact]
    public async Task SyncNow_SomeScansRejected_MarksThemFailedAndRestSynced()
    {
        // Arrange
        var good = Save();
        var bad = Save();
        AcceptAll(bad.Id);

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(good.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(bad.Id)!.SyncStatus);
        Assert.Contains("before the shipment was created", Assert.Single(report.Failures).Reason);
    }

    [Fact]
    public async Task SyncNow_OneShipmentRefused_OtherShipmentsStillSync()
    {
        // Arrange
        var unknown = Save("APX-99999999", minutesAgo: 20);
        var known = Save("APX-10000001", minutesAgo: 10);
        _harness.Server
            .Then(HttpStatusCode.NotFound, """{"title":"Shipment not found","status":404,"detail":"No shipment has tracking number APX-99999999."}""", "application/problem+json")
            .Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(unknown.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(known.Id)!.SyncStatus);
        Assert.Equal("No shipment has tracking number APX-99999999.", Assert.Single(report.Failures).Reason);
    }

    [Fact]
    public async Task SyncNow_ServerDown_LeavesScansPendingAndStops()
    {
        // Arrange
        Save("APX-10000001", minutesAgo: 20);
        Save("APX-10000002", minutesAgo: 10);
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(2, report.LeftPending);
        Assert.NotNull(report.StopReason);
        Assert.Equal(2, _store.Repository.FindPending().Count);
        Assert.Equal(4, _harness.Server.Calls);
    }

    [Fact]
    public async Task ConnectivityChanged_OfflineThenOnline_UploadsOnReconnect()
    {
        // Arrange
        _connectivity.Set(false);
        Save();
        Save();
        AcceptAll();

        // Act
        var offline = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(0, _harness.Server.Calls);
        Assert.Contains("offline", offline.StopReason);

        // Arrange (next step)
        await Wait.UntilAsync(() => _store.Database.PendingCount == 2);

        _connectivity.Set(true);

        // Act
        await _sync.BackgroundRun!;

        // Assert
        Assert.Empty(_store.Repository.FindPending());

        // Act
        await Wait.UntilAsync(() => _store.Database.PendingCount == 0);

        // Assert
        Assert.Equal(2, _sync.LastReport!.Synced);
    }

    [Fact]
    public async Task SyncNow_ServerAlreadyHasScan_CountsAsSynced()
    {
        // Arrange
        var scan = Save();
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":0,"duplicatesSkipped":1,"rejected":0,"rejections":[]}""");

        // Act
        await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task SyncNow_CalledTwiceAtOnce_RunsOnce()
    {
        // Arrange
        Save();
        var release = new TaskCompletionSource();
        _harness.Server.Then(async _ =>
        {
            await release.Task;
            return ScriptedServer.Respond(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0}""");
        });

        var first = _sync.SyncPendingAsync();
        await Wait.UntilAsync(() => _harness.Server.Calls == 1);
        var second = await _sync.SyncPendingAsync();
        release.SetResult();

        // Act and assert
        Assert.Equal("A sync is already running.", second.StopReason);
        Assert.Equal(1, (await first).Synced);
    }

    [Theory]
    [InlineData("<html><body>Please sign in to the Wi-Fi</body></html>")]
    [InlineData("{\"accepted\":")]
    public async Task SyncNow_CaptivePortalHtml_LeavesScansPending(string body)
    {
        // Arrange
        var scan = Save();
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.OK, body, "text/html"));

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Equal(0, report.Failed);
        Assert.Equal(1, report.LeftPending);
        Assert.NotNull(report.StopReason);
    }

    [Fact]
    public async Task SyncNow_ScanEditedDuringUpload_StaysPending()
    {
        // Arrange
        var scan = Save();
        var untouched = Save(minutesAgo: 5);
        _harness.Server.Then(async request =>
        {
            _store.Repository.Update(_store.Repository.Get(scan.Id)! with { Location = "Corrected location" });
            return ScriptedServer.Respond(HttpStatusCode.OK, """{"accepted":2,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        });

        // Act
        var report = await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Equal("Corrected location", _store.Repository.Get(scan.Id)!.Location);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(untouched.Id)!.SyncStatus);
        Assert.Equal((1, 1), (report.Synced, report.LeftPending));
    }

    [Fact]
    public async Task SyncNow_ScanDeletedDuringUpload_DoesNotComeBack()
    {
        // Arrange
        var scan = Save();
        _harness.Server.Then(async request =>
        {
            _store.Repository.Delete(scan.Id);
            return ScriptedServer.Respond(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        });

        // Act
        await _sync.SyncPendingAsync();

        // Assert
        Assert.Null(_store.Repository.Get(scan.Id));
    }

    [Fact]
    public async Task RetryFailed_AfterCauseFixed_SendsFailedScansAgain()
    {
        // Arrange
        var scan = Save("APX-10000009");
        _harness.Server.Then(HttpStatusCode.NotFound, """{"status":404,"detail":"No shipment has tracking number APX-10000009."}""");

        // Act
        await _sync.SyncPendingAsync();

        // Assert
        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Empty(_store.Repository.FindPending());

        // Arrange (next step)
        // A back-office user has created the shipment since.
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");

        // Act
        var report = await _sync.RetryFailedAsync();

        // Assert
        Assert.Equal(1, report.Synced);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task PartAOutbox_RealUploaderUnreachable_LeavesScansPending()
    {
        // Arrange
        var first = Save(minutesAgo: 10);
        var second = Save(minutesAgo: 5);
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));
        var outbox = new OutboxService(_store.Repository, new ApiShipmentUploader(_harness.Client));

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal((0, 0, 2), (result.Synced, result.Failed, result.Deferred));
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(first.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(second.Id)!.SyncStatus);
    }

    [Fact]
    public async Task PartAOutbox_RealUploader_SyncsScans()
    {
        // Arrange
        var scan = Save();
        AcceptAll();
        var outbox = new OutboxService(_store.Repository, new ApiShipmentUploader(_harness.Client));

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(1, result.Synced);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }
}
