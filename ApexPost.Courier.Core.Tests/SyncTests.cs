using System.Net;
using System.Text.Json;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApexPost.Courier.Core.Tests;

public sealed class SyncTests : IDisposable
{
    private readonly ClientHarness _harness = new();
    private readonly TempCourierDatabase _store = new();
    private readonly FakeConnectivity _connectivity = new();
    private readonly ScanSyncService _sync;

    public SyncTests() =>
        _sync = new ScanSyncService(_store.Repository, () => new ApiShipmentUploader(_harness.Client), _connectivity, TimeProvider.System, NullLogger<ScanSyncService>.Instance);

    public void Dispose()
    {
        _sync.Dispose();
        _store.Dispose();
        _harness.Dispose();
    }

    private ShipmentScan Save(string tracking = "APX-10000001", int minutesAgo = 10, ScanType type = ScanType.Pickup) =>
        _store.Repository.Create(ShipmentScan.CreatePending(tracking, type, "CR-1001", "Depot", DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)));

    // The fake server accepts everything except the scan ids in rejectIds.
    private void AcceptAll(params string[] rejectIds) => _harness.Server.Fallback = async request =>
    {
        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
        var ids = body.RootElement.GetProperty("scans").EnumerateArray().Select(scan => scan.GetProperty("scanId").GetString()!).ToList();
        var rejected = ids.Where(rejectIds.Contains).Select(id => $$"""{"scanId":"{{id}}","reason":"The scan time is before the shipment was created."}""");
        return ScriptedServer.Respond(HttpStatusCode.OK, $$"""{"accepted":{{ids.Count - rejected.Count()}},"duplicatesSkipped":0,"rejected":{{rejected.Count()}},"rejections":[{{string.Join(",", rejected)}}]}""");
    };

    [Fact]
    public async Task Pending_scans_go_up_in_batches_of_20_split_by_shipment_and_become_synced()
    {
        for (var index = 0; index < 45; index++)
        {
            Save(index % 3 == 0 ? "APX-10000002" : "APX-10000001", minutesAgo: 100 - index);
        }

        AcceptAll();

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(45, report.Synced);
        Assert.Empty(_store.Repository.FindPending());

        // The change listener updates the live count on its own thread.
        await Wait.UntilAsync(() => _store.Database.PendingCount == 0);
        var sizes = _harness.Server.Received.Select(request => JsonDocument.Parse(request.Body!).RootElement.GetProperty("scans").GetArrayLength()).ToList();
        Assert.All(sizes, size => Assert.InRange(size, 1, ScanSyncService.BatchSize));
        Assert.Equal(6, sizes.Count);
        Assert.Equal(45, sizes.Sum());
    }

    [Fact]
    public async Task Rejected_scans_become_failed_with_the_reason_and_the_rest_synced()
    {
        var good = Save();
        var bad = Save();
        AcceptAll(bad.Id);

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(good.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(bad.Id)!.SyncStatus);
        Assert.Contains("before the shipment was created", Assert.Single(report.Failures).Reason);
    }

    [Fact]
    public async Task A_refused_shipment_marks_its_scans_failed_but_other_shipments_still_sync()
    {
        var unknown = Save("APX-99999999", minutesAgo: 20);
        var known = Save("APX-10000001", minutesAgo: 10);
        _harness.Server
            .Then(HttpStatusCode.NotFound, """{"title":"Shipment not found","status":404,"detail":"No shipment has tracking number APX-99999999."}""", "application/problem+json")
            .Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(unknown.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(known.Id)!.SyncStatus);
        Assert.Equal("No shipment has tracking number APX-99999999.", Assert.Single(report.Failures).Reason);
    }

    [Fact]
    public async Task When_the_server_is_down_the_scans_stay_pending_and_the_run_stops()
    {
        Save("APX-10000001", minutesAgo: 20);
        Save("APX-10000002", minutesAgo: 10);
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(2, report.LeftPending);
        Assert.NotNull(report.StopReason);
        Assert.Equal(2, _store.Repository.FindPending().Count);
        Assert.Equal(4, _harness.Server.Calls);
    }

    [Fact]
    public async Task Offline_nothing_is_sent_and_reconnecting_starts_the_upload()
    {
        _connectivity.Set(false);
        Save();
        Save();
        AcceptAll();

        var offline = await _sync.SyncPendingAsync();
        Assert.Equal(0, _harness.Server.Calls);
        Assert.Contains("offline", offline.StopReason);
        await Wait.UntilAsync(() => _store.Database.PendingCount == 2);

        _connectivity.Set(true);
        await _sync.BackgroundRun!;

        Assert.Empty(_store.Repository.FindPending());
        await Wait.UntilAsync(() => _store.Database.PendingCount == 0);
        Assert.Equal(2, _sync.LastReport!.Synced);
    }

    [Fact]
    public async Task A_duplicate_the_server_already_has_counts_as_synced()
    {
        var scan = Save();
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":0,"duplicatesSkipped":1,"rejected":0,"rejections":[]}""");

        await _sync.SyncPendingAsync();

        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task Only_one_run_at_a_time()
    {
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

        Assert.Equal("A sync is already running.", second.StopReason);
        Assert.Equal(1, (await first).Synced);
    }

    [Theory]
    [InlineData("<html><body>Please sign in to the Wi-Fi</body></html>")]
    [InlineData("{\"accepted\":")]
    public async Task A_captive_portal_page_leaves_the_scans_pending_instead_of_failing_them(string body)
    {
        var scan = Save();
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.OK, body, "text/html"));

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Equal(0, report.Failed);
        Assert.Equal(1, report.LeftPending);
        Assert.NotNull(report.StopReason);
    }

    [Fact]
    public async Task A_scan_edited_while_it_uploads_stays_pending_so_the_edit_is_sent_too()
    {
        var scan = Save();
        var untouched = Save(minutesAgo: 5);
        _harness.Server.Then(async request =>
        {
            _store.Repository.Update(_store.Repository.Get(scan.Id)! with { Location = "Corrected location" });
            return ScriptedServer.Respond(HttpStatusCode.OK, """{"accepted":2,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        });

        var report = await _sync.SyncPendingAsync();

        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Equal("Corrected location", _store.Repository.Get(scan.Id)!.Location);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(untouched.Id)!.SyncStatus);
        Assert.Equal((1, 1), (report.Synced, report.LeftPending));
    }

    [Fact]
    public async Task A_scan_deleted_while_it_uploads_does_not_come_back()
    {
        var scan = Save();
        _harness.Server.Then(async request =>
        {
            _store.Repository.Delete(scan.Id);
            return ScriptedServer.Respond(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        });

        await _sync.SyncPendingAsync();

        Assert.Null(_store.Repository.Get(scan.Id));
    }

    [Fact]
    public async Task Failed_scans_can_be_sent_again_once_the_cause_is_fixed()
    {
        var scan = Save("APX-10000009");
        _harness.Server.Then(HttpStatusCode.NotFound, """{"status":404,"detail":"No shipment has tracking number APX-10000009."}""");
        await _sync.SyncPendingAsync();
        Assert.Equal(SyncStatus.Failed, _store.Repository.Get(scan.Id)!.SyncStatus);
        Assert.Empty(_store.Repository.FindPending());

        // A back-office user has created the shipment since.
        _harness.Server.Then(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}""");
        var report = await _sync.RetryFailedAsync();

        Assert.Equal(1, report.Synced);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }

    [Fact]
    public async Task The_part_a_outbox_leaves_scans_pending_when_the_real_uploader_cannot_reach_the_server()
    {
        var first = Save(minutesAgo: 10);
        var second = Save(minutesAgo: 5);
        _harness.Server.Fallback = _ => Task.FromResult(ScriptedServer.Respond(HttpStatusCode.ServiceUnavailable, null));
        var outbox = new OutboxService(_store.Repository, new ApiShipmentUploader(_harness.Client));

        var result = await outbox.SyncPendingAsync();

        Assert.Equal((0, 0, 2), (result.Synced, result.Failed, result.Deferred));
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(first.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Pending, _store.Repository.Get(second.Id)!.SyncStatus);
    }

    [Fact]
    public async Task The_part_a_outbox_works_with_the_real_uploader_instead_of_the_fake()
    {
        var scan = Save();
        AcceptAll();
        var outbox = new OutboxService(_store.Repository, new ApiShipmentUploader(_harness.Client));

        var result = await outbox.SyncPendingAsync();

        Assert.Equal(1, result.Synced);
        Assert.Equal(SyncStatus.Synced, _store.Repository.Get(scan.Id)!.SyncStatus);
    }
}
