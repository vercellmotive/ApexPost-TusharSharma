using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class OutboxCouchbaseTests
{
    [Fact]
    public async Task SyncPending_NewScans_StayPendingUntilRunThenSynced()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var uploader = new FakeShipmentUploader();
        var outbox = new OutboxService(db.Repository, uploader);
        var first = db.Repository.Create(Scans.Make("APX-1"));

        // Act
        var second = db.Repository.Create(Scans.Make("APX-2"));

        // Assert
        Assert.All(db.Repository.GetAll(), scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
        Assert.Empty(uploader.Attempts);

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(2, result.Synced);
        Assert.Equal(0, result.Failed);
        Assert.Equal(SyncStatus.Synced, db.Repository.Get(first.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, db.Repository.Get(second.Id)!.SyncStatus);
        Assert.Empty(db.Repository.FindPending());
    }

    [Fact]
    public async Task AnUploaderThatCannotReachTheServer_LeavesEveryScanPending_AndStopsTheRun()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var first = db.Repository.Create(Scans.Make("APX-00000001"));
        var second = db.Repository.Create(Scans.Make("APX-00000002"));
        var outbox = new OutboxService(db.Repository, new Unreachable());

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal((0, 0, 2), (result.Synced, result.Failed, result.Deferred));
        Assert.Equal(SyncStatus.Pending, db.Repository.Get(first.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Pending, db.Repository.Get(second.Id)!.SyncStatus);
        Assert.Equal(1, Unreachable.Calls);
    }

    private sealed class Unreachable : IShipmentUploader
    {
        public static int Calls { get; private set; }

        public Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new TransientUploadException("No network.");
        }
    }

    [Fact]
    public async Task ARejectedScan_IsMarkedFailed_WithTheReason()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new FakeShipmentUploader());
        var good = db.Repository.Create(Scans.Make("APX-1"));
        var bad = db.Repository.Create(Scans.Make("APX-2", notes: "label torn [FAIL]"));

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([good.Id], result.SyncedIds);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(bad.Id, failure.ScanId);
        Assert.Contains("[fail]", failure.Error, StringComparison.Ordinal);
        Assert.Equal(SyncStatus.Failed, db.Repository.Get(bad.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, db.Repository.Get(good.Id)!.SyncStatus);
    }

    [Fact]
    public async Task AnUploaderThatThrows_FailsThatScanAndTheRunContinues()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new DelegateUploader((scan, _) =>
            scan.TrackingNumber == "APX-1" ? throw new InvalidOperationException("boom") : Task.FromResult(UploadResult.Success)));
        var explodes = db.Repository.Create(Scans.Make("APX-1", at: Scans.Noon));
        var fine = db.Repository.Create(Scans.Make("APX-2", at: Scans.Noon.AddMinutes(1)));

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal("boom", Assert.Single(result.Failures).Error);
        Assert.Equal(SyncStatus.Failed, db.Repository.Get(explodes.Id)!.SyncStatus);
        Assert.Equal(SyncStatus.Synced, db.Repository.Get(fine.Id)!.SyncStatus);
    }

    [Fact]
    public async Task ScansThatAreAlreadySyncedOrFailed_AreNotUploadedAgain()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var uploader = new FakeShipmentUploader();
        var outbox = new OutboxService(db.Repository, uploader);
        var synced = db.Repository.Create(Scans.Make("APX-1"));
        var failed = db.Repository.Create(Scans.Make("APX-2"));
        var pending = db.Repository.Create(Scans.Make("APX-3"));
        db.Repository.UpdateSyncStatus(synced.Id, SyncStatus.Synced);
        db.Repository.UpdateSyncStatus(failed.Id, SyncStatus.Failed);

        // Act
        await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([pending.Id], uploader.Attempts);
        Assert.Equal(SyncStatus.Failed, db.Repository.Get(failed.Id)!.SyncStatus);
    }

    [Fact]
    public async Task SyncPending_SeveralScans_UploadsOldestFirst()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var uploader = new FakeShipmentUploader();
        var outbox = new OutboxService(db.Repository, uploader);
        var third = db.Repository.Create(Scans.Make("APX-3", at: Scans.Noon.AddHours(2)));
        var first = db.Repository.Create(Scans.Make("APX-1", at: Scans.Noon));
        var second = db.Repository.Create(Scans.Make("APX-2", at: Scans.Noon.AddHours(1)));

        // Act
        await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([first.Id, second.Id, third.Id], uploader.Attempts);
    }

    [Fact]
    public async Task RunningTheOutboxTwice_UploadsEachScanOnce()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var uploader = new FakeShipmentUploader();
        var outbox = new OutboxService(db.Repository, uploader);
        db.Repository.Create(Scans.Make("APX-1"));

        await outbox.SyncPendingAsync();

        // Act
        var second = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal(0, second.Synced + second.Failed + second.Skipped);
        Assert.Single(uploader.Attempts);
    }

    [Fact]
    public async Task AScanDeletedWhileItUploads_IsSkippedAndDoesNotComeBack()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new DelegateUploader((scan, _) =>
        {
            db.Repository.Delete(scan.Id);
            return Task.FromResult(UploadResult.Success);
        }));
        var scan = db.Repository.Create(Scans.Make());

        // Act
        var result = await outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([scan.Id], result.SkippedIds);
        Assert.Equal(0, result.Synced);
        Assert.Null(db.Repository.Get(scan.Id));
        Assert.Empty(db.Repository.GetAll());
    }

    [Fact]
    public async Task AnEditMadeWhileTheScanUploads_IsKept()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var outbox = new OutboxService(db.Repository, new DelegateUploader((scan, _) =>
        {
            db.Repository.Update(scan with { Notes = "corrected during upload" });
            return Task.FromResult(UploadResult.Success);
        }));
        var scan = db.Repository.Create(Scans.Make(notes: "original"));

        await outbox.SyncPendingAsync();

        // Act
        var read = db.Repository.Get(scan.Id)!;

        // Assert
        Assert.Equal(SyncStatus.Synced, read.SyncStatus);
        Assert.Equal("corrected during upload", read.Notes);
    }

    [Fact]
    public async Task Cancelling_StopsTheRun_AndLeavesTheUnfinishedScansPending()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        using var cancellation = new CancellationTokenSource();
        var outbox = new OutboxService(db.Repository, new DelegateUploader((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(UploadResult.Success);
        }));
        db.Repository.Create(Scans.Make("APX-1", at: Scans.Noon));
        db.Repository.Create(Scans.Make("APX-2", at: Scans.Noon.AddMinutes(1)));

        // Act and assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => outbox.SyncPendingAsync(cancellation.Token));

        Assert.Equal(2, db.Repository.FindPending().Count);
        Assert.DoesNotContain(db.Repository.GetAll(), scan => scan.SyncStatus == SyncStatus.Failed);
    }

    private sealed class DelegateUploader(Func<ShipmentScan, CancellationToken, Task<UploadResult>> upload) : IShipmentUploader
    {
        public Task<UploadResult> UploadAsync(ShipmentScan scan, CancellationToken cancellationToken = default) =>
            upload(scan, cancellationToken);
    }
}
