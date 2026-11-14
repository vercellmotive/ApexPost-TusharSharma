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
    public void Allowed_moves_follow_the_status_path(ShipmentStatus from, ShipmentStatus to) =>
        Assert.True(Shipment.CanMove(from, to));

    [Fact]
    public void Every_other_move_is_refused()
    {
        var allowed = AllowedMoves.Select(row => ((ShipmentStatus)row[0], (ShipmentStatus)row[1])).ToHashSet();
        foreach (var from in Enum.GetValues<ShipmentStatus>())
        {
            foreach (var to in Enum.GetValues<ShipmentStatus>())
            {
                Assert.Equal(allowed.Contains((from, to)), Shipment.CanMove(from, to));
            }
        }
    }

    [Fact]
    public void MoveTo_refuses_a_skipped_status_and_leaves_the_shipment_unchanged()
    {
        var shipment = Make.Shipment();

        Assert.Throws<InvalidOperationException>(() => shipment.MoveTo(ShipmentStatus.Delivered, TestClock.Start));
        Assert.Equal(ShipmentStatus.Created, shipment.Status);
    }

    [Theory]
    [InlineData(ShipmentStatus.Delivered)]
    [InlineData(ShipmentStatus.Failed)]
    public void A_closed_shipment_keeps_its_details(ShipmentStatus status)
    {
        var shipment = Make.ShipmentIn(status);

        Assert.True(shipment.IsClosed);
        Assert.Throws<InvalidOperationException>(() => shipment.ReplaceDetails(Make.Recipient("Perth"), 1m, TestClock.Start));
        Assert.Equal("Sydney", shipment.City);
    }

    [Theory]
    [InlineData(ShipmentStatus.Created, true)]
    [InlineData(ShipmentStatus.PickedUp, false)]
    [InlineData(ShipmentStatus.Delivered, false)]
    public void Only_a_created_shipment_can_be_deleted(ShipmentStatus status, bool expected) =>
        Assert.Equal(expected, Make.ShipmentIn(status).CanBeDeleted);

    [Theory]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData("apx-12345678")]
    [InlineData("APX-1234567٣")]
    [InlineData(" APX-12345678")]
    public void Create_refuses_a_bad_tracking_number(string trackingNumber) =>
        Assert.Throws<ArgumentException>(() => Shipment.Create(trackingNumber, "B001", Make.Recipient(), 1m, TestClock.Start));

    [Theory]
    [InlineData("0.09")]
    [InlineData("70.001")]
    public void Create_refuses_a_weight_out_of_range(string weight) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Shipment.Create("APX-12345678", "B001", Make.Recipient(), decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture), TestClock.Start));

    [Fact]
    public void Scan_ids_round_trip_only_in_the_part_a_format()
    {
        var id = Guid.NewGuid();

        Assert.True(ScanId.TryParse(ScanId.Format(id), out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(ScanId.TryParse(id.ToString(), out _));
        Assert.False(ScanId.TryParse(ScanId.Format(id).ToUpperInvariant(), out _));
        Assert.False(ScanId.TryParse($"scan::{id:N}", out _));
    }
}
