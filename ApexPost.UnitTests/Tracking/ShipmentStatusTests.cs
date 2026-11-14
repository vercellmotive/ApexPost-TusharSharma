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

    [Fact]
    public void CanMove_DisallowedPair_ReturnsFalse()
    {
        // Act
        var allowed = AllowedMoves.Select(row => ((ShipmentStatus)row[0], (ShipmentStatus)row[1])).ToHashSet();

        // Assert
        foreach (var from in Enum.GetValues<ShipmentStatus>())
        {
            foreach (var to in Enum.GetValues<ShipmentStatus>())
            {
                Assert.Equal(allowed.Contains((from, to)), Shipment.CanMove(from, to));
            }
        }
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

    [Theory]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData("apx-12345678")]
    [InlineData("APX-1234567٣")]
    [InlineData(" APX-12345678")]
    public void Create_BadTrackingNumber_Throws(string trackingNumber)
    {
        // Act
        Action act = () => Shipment.Create(trackingNumber, "B001", Make.Recipient(), 1m, TestTime.Start);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Theory]
    [InlineData("0.09")]
    [InlineData("70.001")]
    public void Create_WeightOutOfRange_Throws(string weight)
    {
        // Arrange
        var weightKg = decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture);

        // Act
        Action act = () => Shipment.Create("APX-12345678", "B001", Make.Recipient(), weightKg, TestTime.Start);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
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
