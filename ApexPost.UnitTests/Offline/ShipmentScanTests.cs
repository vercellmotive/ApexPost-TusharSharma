using ApexPost.Courier.Offline.Domain;
using ApexPost.Testing.Offline;

namespace ApexPost.UnitTests.Offline;

public class ShipmentScanTests
{
    [Fact]
    public void NewId_Called_ReturnsScanPrefixAndALowerCaseGuid()
    {
        // Act
        var id = ShipmentScan.NewId();

        // Assert
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
    public void IsValidId_CandidateId_AcceptsOnlyTheScanGuidFormat(string? id, bool expected)
    {
        // Act
        var valid = ShipmentScan.IsValidId(id);

        // Assert
        Assert.Equal(expected, valid);
    }

    [Fact]
    public void CreatePending_StartsPending_WithAFreshId()
    {
        // Arrange
        var first = Scans.Make();

        // Act
        var second = Scans.Make();

        // Assert
        Assert.Equal(SyncStatus.Pending, first.SyncStatus);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(ShipmentScan.IsValidId(first.Id));
    }

    [Fact]
    public void CreatePending_ConvertsTheTimeToUtc_KeepingTheInstant()
    {
        // Arrange
        var sydney = new DateTimeOffset(2026, 11, 2, 22, 30, 0, TimeSpan.FromHours(11));

        // Act
        var scan = Scans.Make(at: sydney);

        // Assert
        Assert.Equal(TimeSpan.Zero, scan.ScannedAtUtc.Offset);
        Assert.Equal(sydney.UtcDateTime, scan.ScannedAtUtc.UtcDateTime);
    }

    [Fact]
    public void CreatePending_PaddedTextAndBlankNotes_TrimsTextAndStoresNullNotes()
    {
        // Act
        var scan = ShipmentScan.CreatePending("  APX-1  ", ScanType.Delivered, " C-7 ", "  Perth WA ", Scans.Noon, "   ");

        // Assert
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
    public void CreatePending_BlankTrackingNumberCourierOrLocation_Throws(string tracking, string courier, string location)
    {
        // Act
        Action act = () => ShipmentScan.CreatePending(tracking, ScanType.Pickup, courier, location, Scans.Noon);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public void CreatePending_TextOverTheMaximumLength_Throws()
    {
        // Act
        Action longTrackingNumber = () => Scans.Make(trackingNumber: new string('A', ShipmentScan.MaxTrackingNumberLength + 1));
        Action longCourierId = () => Scans.Make(courierId: new string('C', ShipmentScan.MaxCourierIdLength + 1));
        Action longLocation = () => Scans.Make(location: new string('L', ShipmentScan.MaxLocationLength + 1));
        Action longNotes = () => Scans.Make(notes: new string('N', ShipmentScan.MaxNotesLength + 1));

        // Assert
        Assert.Throws<ArgumentException>(longTrackingNumber);
        Assert.Throws<ArgumentException>(longCourierId);
        Assert.Throws<ArgumentException>(longLocation);
        Assert.Throws<ArgumentException>(longNotes);
    }

    [Fact]
    public void Validate_NonUtcTimeUnknownEnumOrBadId_Throws()
    {
        // Arrange
        var good = Scans.Make();

        // Act
        Action nonUtcTime = () => (good with { ScannedAtUtc = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(10)) }).Validate();
        Action unknownScanType = () => (good with { ScanType = (ScanType)99 }).Validate();
        Action unknownSyncStatus = () => (good with { SyncStatus = (SyncStatus)99 }).Validate();
        Action badId = () => (good with { Id = "scan::123" }).Validate();
        var goodError = Record.Exception(() => good.Validate());

        // Assert
        Assert.Throws<ArgumentException>(nonUtcTime);
        Assert.Throws<ArgumentException>(unknownScanType);
        Assert.Throws<ArgumentException>(unknownSyncStatus);
        Assert.Throws<ArgumentException>(badId);
        Assert.Null(goodError);
    }

    [Fact]
    public void Enums_Names_MatchTheBrief()
    {
        // Act
        var scanTypes = Enum.GetNames<ScanType>();
        var syncStatuses = Enum.GetNames<SyncStatus>();

        // Assert
        Assert.Equal(["Pickup", "InTransit", "Delivered", "Failed"], scanTypes);
        Assert.Equal(["Pending", "Synced", "Failed"], syncStatuses);
    }
}
