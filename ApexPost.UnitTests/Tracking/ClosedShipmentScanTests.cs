using ApexPost.Application.Interfaces;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ApexPost.UnitTests.Tracking;

/// <summary>
/// R8, built test-first (TDD-LOG.md): once a shipment is Delivered or Failed it is closed, and a scan recorded after
/// that moment is rejected with a reason, so a late upload cannot make a delivered parcel appear to move again.
/// Scans recorded before the shipment closed still count: a courier who was offline uploads them late.
/// </summary>
public sealed class ClosedShipmentScanTests
{
    private static readonly DateTimeOffset ClosedAt = TestTime.Start.AddHours(2);

    private readonly Mock<IShipmentStore> _store = new();
    private readonly FakeTimeProvider _clock = new(ClosedAt.AddHours(1));
    private readonly ShipmentService _service;

    public ClosedShipmentScanTests()
    {
        _store.Setup(store => store.FindScanShipmentsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _store.Setup(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TrackingScan> scans, CancellationToken _) => scans.Select(scan => scan.ScanId).ToHashSet());
        _service = new ShipmentService(_store.Object, Mock.Of<IBranchRepository>(), _clock);
    }

    /// <summary>A shipment that went through every step and was closed at <see cref="ClosedAt"/>.</summary>
    private void GivenShipmentClosedAs(ShipmentStatus finalStatus)
    {
        var shipment = Shipment.Create("APX-10000001", "B001", Make.Recipient(), 2.5m, TestTime.Start);
        shipment.MoveTo(ShipmentStatus.PickedUp, TestTime.Start.AddMinutes(10));
        shipment.MoveTo(ShipmentStatus.InTransit, TestTime.Start.AddMinutes(20));
        shipment.MoveTo(ShipmentStatus.OutForDelivery, TestTime.Start.AddMinutes(30));
        shipment.MoveTo(finalStatus, ClosedAt);
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Failed)]
    public async Task UploadScansAsync_ScanRecordedAfterShipmentClosed_IsRejectedWithReason(ShipmentStatus finalStatus)
    {
        // Arrange
        GivenShipmentClosedAs(finalStatus);
        var late = Make.Scan(type: ScanType.InTransit, at: ClosedAt.AddMinutes(15));

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [late], Make.CourierId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value.Accepted);
        var rejection = Assert.Single(result.Value.Rejections);
        Assert.Equal(late.ScanId, rejection.ScanId);
        Assert.Equal($"The shipment was closed as {finalStatus} before this scan was recorded.", rejection.Reason);
        _store.Verify(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(-60, true)]   // recorded an hour before the shipment closed: a late upload of an early scan
    [InlineData(0, true)]     // recorded at the very moment it closed: the closing scan itself
    [InlineData(1, false)]    // one second after it closed
    [InlineData(3600, false)]
    public async Task UploadScansAsync_ScanTimeAroundTheClosingTime_AcceptedOnlyUpToTheClose(int secondsAfterClose, bool accepted)
    {
        // Arrange
        GivenShipmentClosedAs(ShipmentStatus.Delivered);
        var scan = Make.Scan(type: ScanType.InTransit, at: ClosedAt.AddSeconds(secondsAfterClose));

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan], Make.CourierId);

        // Assert
        Assert.Equal(accepted ? 1 : 0, result.Value!.Accepted);
        Assert.Equal(accepted ? 0 : 1, result.Value.Rejections.Count);
    }

    [Fact]
    public async Task UploadScansAsync_ShipmentStillOpen_AcceptsAScanOfAnyTime()
    {
        // Arrange
        var shipment = Make.ShipmentIn(ShipmentStatus.OutForDelivery);
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        var scan = Make.Scan(type: ScanType.InTransit, at: _clock.GetUtcNow());

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan], Make.CourierId);

        // Assert
        Assert.Equal(1, result.Value!.Accepted);
        Assert.Empty(result.Value.Rejections);
    }
}
