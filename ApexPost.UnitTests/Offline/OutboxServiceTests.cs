// Copilot-assisted: first drafted by GitHub Copilot (COPILOT-LOG.md, prompt 1), then reviewed and changed by hand.
// Kept: the six scenarios and their Moq style. Changed: the repository mock now confirms status updates (the draft
// left UpdateSyncStatus unset, so Moq returned false and three tests failed), fictional data in the real formats,
// the cancellation test's Act and Assert are separated, and two missing cases were added.
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Sync;
using Moq;

namespace ApexPost.UnitTests.Offline;

/// <summary>
/// R6: the Part A outbox with its repository and uploader mocked. These tests prove the outbox's decisions (Synced,
/// Failed, left Pending, skipped). They do not prove that the repository really finds the pending scans oldest first
/// or stores a status: that is OutboxCouchbaseTests, against the real embedded database.
/// </summary>
public sealed class OutboxServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IScanRepository> _repository = new();
    private readonly Mock<IShipmentUploader> _uploader = new();
    private readonly OutboxService _outbox;

    public OutboxServiceTests()
    {
        // A stored scan's status can be changed. The one test about a deleted scan overrides this.
        _repository.Setup(repository => repository.UpdateSyncStatus(It.IsAny<string>(), It.IsAny<SyncStatus>())).Returns(true);
        _outbox = new OutboxService(_repository.Object, _uploader.Object);
    }

    private static ShipmentScan Scan(string trackingNumber, int minutes = 0) =>
        ShipmentScan.CreatePending(trackingNumber, ScanType.InTransit, "CR-1001", "Sydney depot", Noon.AddMinutes(minutes));

    private void GivenPending(params ShipmentScan[] scans) => _repository.Setup(repository => repository.FindPending()).Returns(scans);

    [Fact]
    public async Task SyncPendingAsync_SuccessfulUpload_MarksScanSynced()
    {
        // Arrange
        var scan = Scan("APX-60000001");
        GivenPending(scan);
        _uploader.Setup(uploader => uploader.UploadAsync(It.Is<ShipmentScan>(s => s.Id == scan.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadResult.Success);

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([scan.Id], result.SyncedIds);
        Assert.Empty(result.Failures);
        _repository.Verify(repository => repository.UpdateSyncStatus(scan.Id, SyncStatus.Synced), Times.Once);
        // Only the status changes, never the whole scan, so an edit made during the upload is kept.
        _repository.Verify(repository => repository.Update(It.IsAny<ShipmentScan>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_RejectedUpload_MarksScanFailedWithError()
    {
        // Arrange
        var scan = Scan("APX-60000002");
        const string error = "No shipment has tracking number APX-60000002.";
        GivenPending(scan);
        _uploader.Setup(uploader => uploader.UploadAsync(It.Is<ShipmentScan>(s => s.Id == scan.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadResult.Failure(error));

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Empty(result.SyncedIds);
        Assert.Equal(new OutboxFailure(scan.Id, error), Assert.Single(result.Failures));
        _repository.Verify(repository => repository.UpdateSyncStatus(scan.Id, SyncStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task SyncPendingAsync_UploaderThrows_MarksScanFailedAndContinuesRun()
    {
        // Arrange
        var failing = Scan("APX-60000003");
        var succeeding = Scan("APX-60000004", minutes: 1);
        GivenPending(failing, succeeding);
        _uploader.Setup(uploader => uploader.UploadAsync(It.Is<ShipmentScan>(s => s.Id == failing.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected upload error."));
        _uploader.Setup(uploader => uploader.UploadAsync(It.Is<ShipmentScan>(s => s.Id == succeeding.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadResult.Success);

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([succeeding.Id], result.SyncedIds);
        Assert.Equal(new OutboxFailure(failing.Id, "Unexpected upload error."), Assert.Single(result.Failures));
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SyncPendingAsync_UploaderReturnsNoResult_MarksScanFailedInsteadOfCrashing()
    {
        // Arrange
        // Added in review: a missing case. A badly written uploader could return null.
        var scan = Scan("APX-60000005");
        GivenPending(scan);
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>())).ReturnsAsync((UploadResult)null!);

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Equal("The uploader returned no result.", Assert.Single(result.Failures).Error);
        _repository.Verify(repository => repository.UpdateSyncStatus(scan.Id, SyncStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task SyncPendingAsync_TransientUploadException_LeavesScansPendingAndStopsRun()
    {
        // Arrange
        var first = Scan("APX-60000006");
        var second = Scan("APX-60000007", minutes: 1);
        GivenPending(first, second);
        _uploader.Setup(uploader => uploader.UploadAsync(It.Is<ShipmentScan>(s => s.Id == first.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientUploadException("Server unavailable."));

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Empty(result.SyncedIds);
        Assert.Empty(result.Failures);
        Assert.Equal([first.Id, second.Id], result.DeferredIds);
        _repository.Verify(repository => repository.UpdateSyncStatus(It.IsAny<string>(), It.IsAny<SyncStatus>()), Times.Never);
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncPendingAsync_ServerLostAfterFirstUpload_KeepsTheFirstSyncedAndDefersTheRest()
    {
        // Arrange
        // Added in review: the draft only lost the server on the first scan. Here one scan gets through first.
        var first = Scan("APX-60000008");
        var second = Scan("APX-60000009", minutes: 1);
        _repository.SetupSequence(repository => repository.FindPending())
            .Returns([first, second])   // the run's list
            .Returns([second]);         // what is still pending when the server is lost
        _uploader.SetupSequence(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UploadResult.Success)
            .ThrowsAsync(new TransientUploadException("Server unavailable."));

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([first.Id], result.SyncedIds);
        Assert.Equal([second.Id], result.DeferredIds);
        _repository.Verify(repository => repository.UpdateSyncStatus(second.Id, It.IsAny<SyncStatus>()), Times.Never);
    }

    [Fact]
    public async Task SyncPendingAsync_ScanDeletedDuringUpload_SkipsScan()
    {
        // Arrange
        var scan = Scan("APX-60000010");
        GivenPending(scan);
        _repository.Setup(repository => repository.UpdateSyncStatus(scan.Id, SyncStatus.Synced)).Returns(false);
        _uploader.Setup(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>())).ReturnsAsync(UploadResult.Success);

        // Act
        var result = await _outbox.SyncPendingAsync();

        // Assert
        Assert.Equal([scan.Id], result.SkippedIds);
        Assert.Empty(result.SyncedIds);
    }

    [Fact]
    public async Task SyncPendingAsync_CancellationRequested_ThrowsAndUploadsNothing()
    {
        // Arrange
        GivenPending(Scan("APX-60000011"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        Func<Task> act = () => _outbox.SyncPendingAsync(cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        _uploader.Verify(uploader => uploader.UploadAsync(It.IsAny<ShipmentScan>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(repository => repository.UpdateSyncStatus(It.IsAny<string>(), It.IsAny<SyncStatus>()), Times.Never);
    }
}
