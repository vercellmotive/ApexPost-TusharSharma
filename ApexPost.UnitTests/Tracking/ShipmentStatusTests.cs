using ApexPost.Tracking.Domain;
using ApexPost.Testing.Tracking;

namespace ApexPost.UnitTests.Tracking;

public sealed class ShipmentStatusTests
{
    public static TheoryData<ShipmentStatus, ShipmentStatus> AllowedMoves => new()
    {
        { ShipmentStatus.Created, ShipmentStatus.PickedUp },
        { ShipmentStatus.PickedUp, ShipmentStatus.InTransit },
        { ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery },
        { ShipmentStatus.OutForDelivery, ShipmentStatus.Delivered },
        { ShipmentStatus.OutForDelivery, ShipmentStatus.Failed },
    };

    [Theory]
    [MemberData(nameof(AllowedMoves))]
    public void CanMove_AllowedPair_ReturnsTrue(ShipmentStatus from, ShipmentStatus to)
    {
        // Act
        var allowed = Shipment.CanMove(from, to);

        // Assert
        Assert.True(allowed);
    }

    /// <summary>Every from/to pair that is not in <see cref="AllowedMoves"/>: 31 of the 36.</summary>
    public static TheoryData<ShipmentStatus, ShipmentStatus> DisallowedMoves()
    {
        var allowed = AllowedMoves.Select(row => ((ShipmentStatus)row[0], (ShipmentStatus)row[1])).ToHashSet();
        var data = new TheoryData<ShipmentStatus, ShipmentStatus>();
        foreach (var from in Enum.GetValues<ShipmentStatus>())
        {
            foreach (var to in Enum.GetValues<ShipmentStatus>().Where(to => !allowed.Contains((from, to))))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DisallowedMoves))]
    public void CanMove_DisallowedPair_ReturnsFalse(ShipmentStatus from, ShipmentStatus to)
    {
        // Act
        var allowed = Shipment.CanMove(from, to);

        // Assert
        Assert.False(allowed);
    }

    [Fact]
    public void MoveTo_DeliveredBackToInTransit_ThrowsInvalidOperationException()
    {
        // Arrange
        var shipment = Make.ShipmentIn(ShipmentStatus.Delivered);

        // Act
        Action act = () => shipment.MoveTo(ShipmentStatus.InTransit, TestTime.Start.AddHours(3));

        // Assert
        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("from Delivered to InTransit", exception.Message);
        Assert.Equal(ShipmentStatus.Delivered, shipment.Status);
    }

    [Fact]
    public void MoveTo_SkippedStatus_ThrowsAndLeavesShipmentUnchanged()
    {
        // Arrange
        var shipment = Make.Shipment();

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => shipment.MoveTo(ShipmentStatus.Delivered, TestTime.Start));
        Assert.Equal(ShipmentStatus.Created, shipment.Status);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Failed)]
    public void ReplaceDetails_ClosedShipment_ThrowsAndKeepsDetails(ShipmentStatus status)
    {
        // Arrange
        var shipment = Make.ShipmentIn(status);

        // Act and assert
        Assert.True(shipment.IsClosed);
        Assert.Throws<InvalidOperationException>(() => shipment.ReplaceDetails(Make.Recipient("Perth"), 1m, TestTime.Start));
        Assert.Equal("Sydney", shipment.City);
    }

    [Theory]
    [InlineData(ShipmentStatus.Created, true)]
    [InlineData(ShipmentStatus.PickedUp, false)]
    [InlineData(ShipmentStatus.Delivered, false)]
    public void CanBeDeleted_ByStatus_TrueOnlyWhenCreated(ShipmentStatus status, bool expected)
    {
        // Arrange
        var shipment = Make.ShipmentIn(status);

        // Act
        var deletable = shipment.CanBeDeleted;

        // Assert
        Assert.Equal(expected, deletable);
    }

    [Fact]
    public void ScanId_TryParse_RoundTripsOnlyPartAFormat()
    {
        // Act
        var id = Guid.NewGuid();

        // Assert
        Assert.True(ScanId.TryParse(ScanId.Format(id), out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(ScanId.TryParse(id.ToString(), out _));
        Assert.False(ScanId.TryParse(ScanId.Format(id).ToUpperInvariant(), out _));
        Assert.False(ScanId.TryParse($"scan::{id:N}", out _));
    }
}
