using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Domain;

namespace ApexPost.UnitTests.Tracking;

/// <summary>
/// R8 at the domain level: the members the REFACTOR step extracted from <see cref="ApexPost.Tracking.Application.ShipmentService"/>
/// (TDD-LOG.md). The service-level tests in <see cref="ClosedShipmentScanTests"/> were written first.
/// </summary>
public sealed class ShipmentClosingTests
{
    private static readonly DateTimeOffset ClosedAt = TestTime.Start.AddHours(2);

    private static Shipment ClosedAs(ShipmentStatus finalStatus)
    {
        var shipment = Shipment.Create("APX-10000001", "B001", Make.Recipient(), 2.5m, TestTime.Start);
        shipment.MoveTo(ShipmentStatus.PickedUp, TestTime.Start.AddMinutes(10));
        shipment.MoveTo(ShipmentStatus.InTransit, TestTime.Start.AddMinutes(20));
        shipment.MoveTo(ShipmentStatus.OutForDelivery, TestTime.Start.AddMinutes(30));
        shipment.MoveTo(finalStatus, ClosedAt);
        return shipment;
    }

    [Theory]
    [InlineData(ShipmentStatus.Created)]
    [InlineData(ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.InTransit)]
    [InlineData(ShipmentStatus.OutForDelivery)]
    public void ClosedAtUtc_OpenShipment_IsNull(ShipmentStatus status)
    {
        // Arrange
        var shipment = Make.ShipmentIn(status);

        // Act
        var closedAt = shipment.ClosedAtUtc;

        // Assert
        Assert.Null(closedAt);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Failed)]
    public void ClosedAtUtc_ClosedShipment_IsTheMomentItClosed(ShipmentStatus finalStatus)
    {
        // Arrange
        var shipment = ClosedAs(finalStatus);

        // Act
        var closedAt = shipment.ClosedAtUtc;

        // Assert
        Assert.Equal(ClosedAt, closedAt);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void AcceptsScanRecordedAt_AroundTheClosingTime_TrueUpToTheClose(int secondsAfterClose, bool expected)
    {
        // Arrange
        var shipment = ClosedAs(ShipmentStatus.Delivered);

        // Act
        var accepted = shipment.AcceptsScanRecordedAt(ClosedAt.AddSeconds(secondsAfterClose));

        // Assert
        Assert.Equal(expected, accepted);
    }

    [Fact]
    public void AcceptsScanRecordedAt_OpenShipment_AcceptsAnyTime()
    {
        // Arrange
        var shipment = Make.ShipmentIn(ShipmentStatus.OutForDelivery);

        // Act
        var accepted = shipment.AcceptsScanRecordedAt(DateTimeOffset.MaxValue);

        // Assert
        Assert.True(accepted);
    }
}
