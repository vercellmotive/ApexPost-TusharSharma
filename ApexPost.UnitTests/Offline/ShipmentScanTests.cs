using ApexPost.Courier.Offline.Domain;
using ApexPost.Testing.Offline;

namespace ApexPost.UnitTests.Offline;

public class ShipmentScanTests
{
    [Fact]
    public void NewId_IsScanPrefixThenACanonicalLowerCaseGuid()
    {
        var id = ShipmentScan.NewId();

        Assert.StartsWith("scan::", id, StringComparison.Ordinal);
        Assert.True(Guid.TryParseExact(id["scan::".Length..], "D", out _));
        Assert.Equal(id, id.ToLowerInvariant());
        Assert.True(ShipmentScan.IsValidId(id));
    }

    [Theory]
    [InlineData("scan::3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("scan::3F2504E0-4F89-11D3-9A0C-0305E82C3301", false)]
    [InlineData("scan::3f2504e04f8911d39a0c0305e82c3301", false)]
    [InlineData("scan::not-a-guid", false)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData("scan:3f2504e0-4f89-11d3-9a0c-0305e82c3301", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidId_AcceptsOnlyTheScanGuidFormat(string? id, bool expected) =>
        Assert.Equal(expected, ShipmentScan.IsValidId(id));

    [Fact]
    public void CreatePending_StartsPending_WithAFreshId()
    {
        var first = Scans.Make();
        var second = Scans.Make();

        Assert.Equal(SyncStatus.Pending, first.SyncStatus);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(ShipmentScan.IsValidId(first.Id));
    }

    [Fact]
    public void CreatePending_ConvertsTheTimeToUtc_KeepingTheInstant()
    {
        var sydney = new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.FromHours(11));

        var scan = Scans.Make(at: sydney);

        Assert.Equal(TimeSpan.Zero, scan.ScannedAtUtc.Offset);
        Assert.Equal(sydney.UtcDateTime, scan.ScannedAtUtc.UtcDateTime);
    }

    [Fact]
    public void CreatePending_TrimsText_AndTurnsBlankNotesIntoNull()
    {
        var scan = ShipmentScan.CreatePending("  APX-1  ", ScanType.Delivered, " C-7 ", "  Perth WA ", Scans.Noon, "   ");

        Assert.Equal("APX-1", scan.TrackingNumber);
        Assert.Equal("C-7", scan.CourierId);
        Assert.Equal("Perth WA", scan.Location);
        Assert.Null(scan.Notes);
    }

    [Theory]
    [InlineData("", "C-1", "Sydney")]
    [InlineData("APX 001", "C-1", "Sydney")]
    [InlineData("APX-001", "", "Sydney")]
    [InlineData("APX-001", "C 1", "Sydney")]
    [InlineData("APX-001", "C-1", "")]
    [InlineData("APX-001", "C-1", "Syd\tney")]
    public void CreatePending_RejectsBadTrackingNumberCourierOrLocation(string tracking, string courier, string location) =>
        Assert.Throws<ArgumentException>(() => ShipmentScan.CreatePending(tracking, ScanType.Pickup, courier, location, Scans.Noon));

    [Fact]
    public void CreatePending_RejectsTextThatIsTooLong()
    {
        Assert.Throws<ArgumentException>(() => Scans.Make(trackingNumber: new string('A', ShipmentScan.MaxTrackingNumberLength + 1)));
        Assert.Throws<ArgumentException>(() => Scans.Make(courierId: new string('C', ShipmentScan.MaxCourierIdLength + 1)));
        Assert.Throws<ArgumentException>(() => Scans.Make(location: new string('L', ShipmentScan.MaxLocationLength + 1)));
        Assert.Throws<ArgumentException>(() => Scans.Make(notes: new string('N', ShipmentScan.MaxNotesLength + 1)));
    }

    [Fact]
    public void Validate_RejectsANonUtcTime_AnUnknownEnum_AndABadId()
    {
        var good = Scans.Make();

        Assert.Throws<ArgumentException>(() => (good with { ScannedAtUtc = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(10)) }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { ScanType = (ScanType)99 }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { SyncStatus = (SyncStatus)99 }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { Id = "scan::123" }).Validate());
        good.Validate();
    }

    [Fact]
    public void TheEnumsUseTheNamesFromTheBrief()
    {
        Assert.Equal(["Pickup", "InTransit", "Delivered", "Failed"], Enum.GetNames<ScanType>());
        Assert.Equal(["Pending", "Synced", "Failed"], Enum.GetNames<SyncStatus>());
    }
}
