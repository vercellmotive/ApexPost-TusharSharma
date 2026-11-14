using System.Reflection;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using Couchbase.Lite.Query;
using ApexPost.Testing.Offline;

namespace ApexPost.IntegrationTests.Offline;

public class ScanQueryTests
{
    [Fact]
    public void FindByTrackingNumber_ReturnsOnlyThatNumber_OrderedByScannedAtUtc()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var delivered = db.Repository.Create(Scans.Make("APX-A", ScanType.Delivered, at: Scans.Noon.AddHours(3)));
        var pickup = db.Repository.Create(Scans.Make("APX-A", ScanType.Pickup, at: Scans.Noon));
        var inTransit = db.Repository.Create(Scans.Make("APX-A", ScanType.InTransit, at: Scans.Noon.AddHours(1)));
        db.Repository.Create(Scans.Make("APX-B", at: Scans.Noon.AddMinutes(5)));

        // Act
        var found = db.Repository.FindByTrackingNumber("APX-A");

        // Assert
        Assert.Equal([pickup.Id, inTransit.Id, delivered.Id], found.Select(scan => scan.Id));
        Assert.All(found, scan => Assert.Equal("APX-A", scan.TrackingNumber));
    }

    [Fact]
    public void FindByTrackingNumber_OrdersByTheInstant_WhateverOffsetTheTimesWereGivenIn()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var later = db.Repository.Create(Scans.Make("APX-A", at: new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(-5))));
        var earlier = db.Repository.Create(Scans.Make("APX-A", at: new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.FromHours(10))));

        // Act
        var found = db.Repository.FindByTrackingNumber("APX-A");

        // Assert
        Assert.Equal([earlier.Id, later.Id], found.Select(scan => scan.Id));
    }

    [Theory]
    [InlineData("APX-A' OR '1'='1")]
    [InlineData("APX-A\" OR 1=1 --")]
    [InlineData("' ; DELETE FROM _ ; --")]
    [InlineData("$trackingNumber")]
    public void FindByTrackingNumber_TreatsWhateverIsTypedAsDataNeverAsQuery(string hostile)
    {
        // Arrange
        using var db = new TestCourierDatabase();
        db.Repository.Create(Scans.Make("APX-A"));

        // Act
        db.Repository.Create(Scans.Make("APX-B"));

        // Assert
        Assert.Empty(db.Repository.FindByTrackingNumber(hostile));
        Assert.Equal(2, db.Repository.GetAll().Count);
    }

    [Fact]
    public void FindByTrackingNumber_ReturnsNothingForAnUnknownNumber_AndRejectsABlankOne()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        db.Repository.Create(Scans.Make("APX-A"));

        // Act and assert
        Assert.Empty(db.Repository.FindByTrackingNumber("APX-NOPE"));
        Assert.Throws<ArgumentException>(() => db.Repository.FindByTrackingNumber(" "));
    }

    [Fact]
    public void FindPending_ReturnsOnlyPendingScans_OldestFirst()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var second = db.Repository.Create(Scans.Make("APX-2", at: Scans.Noon.AddMinutes(2)));
        var synced = db.Repository.Create(Scans.Make("APX-3", at: Scans.Noon.AddMinutes(3)));
        var failed = db.Repository.Create(Scans.Make("APX-4", at: Scans.Noon.AddMinutes(4)));
        var first = db.Repository.Create(Scans.Make("APX-1", at: Scans.Noon.AddMinutes(1)));
        db.Repository.UpdateSyncStatus(synced.Id, SyncStatus.Synced);
        db.Repository.UpdateSyncStatus(failed.Id, SyncStatus.Failed);

        // Act
        var pending = db.Repository.FindPending();

        // Assert
        Assert.Equal([first.Id, second.Id], pending.Select(scan => scan.Id));
        Assert.All(pending, scan => Assert.Equal(SyncStatus.Pending, scan.SyncStatus));
        Assert.Equal(2, db.Repository.CountPending());
    }

    [Fact]
    public void CountByScanType_CountsEachTypeForOneCourierOnly()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        db.Repository.Create(Scans.Make("APX-1", ScanType.Pickup, "C-1"));
        db.Repository.Create(Scans.Make("APX-2", ScanType.Pickup, "C-1"));
        db.Repository.Create(Scans.Make("APX-1", ScanType.Delivered, "C-1"));
        db.Repository.Create(Scans.Make("APX-3", ScanType.InTransit, "C-2"));
        db.Repository.Create(Scans.Make("APX-4", ScanType.Failed, "C-2"));

        var courierOne = db.Repository.CountByScanType("C-1");

        // Act
        var courierTwo = db.Repository.CountByScanType("C-2");

        // Assert
        Assert.Equal(2, courierOne[ScanType.Pickup]);
        Assert.Equal(0, courierOne[ScanType.InTransit]);
        Assert.Equal(1, courierOne[ScanType.Delivered]);
        Assert.Equal(0, courierOne[ScanType.Failed]);
        Assert.Equal(1, courierTwo[ScanType.InTransit]);
        Assert.Equal(1, courierTwo[ScanType.Failed]);
        Assert.Equal(0, courierTwo[ScanType.Pickup]);
    }

    [Fact]
    public void CountByScanType_ShowsEveryTypeAsZeroForACourierWithNoScans()
    {
        // Arrange
        using var db = new TestCourierDatabase();
        db.Repository.Create(Scans.Make(courierId: "C-1"));

        var counts = db.Repository.CountByScanType("C-NOBODY");

        // Act and assert
        Assert.Equal(Enum.GetValues<ScanType>(), counts.Keys);
        Assert.All(counts.Values, count => Assert.Equal(0, count));
        Assert.Throws<ArgumentException>(() => db.Repository.CountByScanType(""));
    }

    [Fact]
    public void EveryStatement_IsAWholeLiteralThatTakesItsValuesAsParameters()
    {
        // Act
        var statements = typeof(ScanQueries)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(text => text.StartsWith("SELECT", StringComparison.Ordinal))
            .ToList();

        // Assert
        Assert.Equal(5, statements.Count);
        Assert.All(statements, sql => Assert.DoesNotContain("'", sql, StringComparison.Ordinal));
        Assert.All(statements.Where(sql => sql.Contains("WHERE", StringComparison.Ordinal)), sql => Assert.Contains(" = $", sql, StringComparison.Ordinal));
    }

    [Fact]
    public void TheTrackingNumberQuery_UsesTheTrackingNumberIndex()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        var plan = Explain(db, ScanQueries.ByTrackingNumber, ScanQueries.TrackingNumberParameter, "APX-1");

        // Assert
        Assert.Contains("USING INDEX idx_trackingNumber", plan, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ScanQueries.ByStatus))]
    [InlineData(nameof(ScanQueries.CountByStatus))]
    public void TheSyncStatusQueries_UseTheSyncStatusIndex(string statement)
    {
        // Arrange
        using var db = new TestCourierDatabase();
        var sql = (string)typeof(ScanQueries).GetField(statement)!.GetRawConstantValue()!;

        // Act
        var plan = Explain(db, sql, ScanQueries.StatusParameter, "Pending");

        // Assert
        Assert.Contains("USING INDEX idx_syncStatus", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainQueries_ShowsWhichIndexEachStatementUses()
    {
        // Arrange
        using var db = new TestCourierDatabase();

        // Act
        var plans = db.Repository.ExplainQueries().ToDictionary(plan => plan.Name, plan => plan.AccessPath);

        // Assert
        Assert.Equal(4, plans.Count);
        Assert.Contains("USING INDEX idx_trackingNumber", plans["All scans for a tracking number"], StringComparison.Ordinal);
        Assert.Contains("USING INDEX idx_syncStatus", plans["All scans where syncStatus = Pending"], StringComparison.Ordinal);
        Assert.Contains("USING INDEX idx_syncStatus", plans["Number of pending scans"], StringComparison.Ordinal);

        // courierId is not indexed on purpose: one device holds one courier's scans (see OFFLINE-DESIGN.md).
        Assert.Contains("SCAN", plans["Number of scans per scanType for one courier"], StringComparison.Ordinal);
        Assert.DoesNotContain("INDEX", plans["Number of scans per scanType for one courier"], StringComparison.Ordinal);
    }

    private static string Explain(TestCourierDatabase db, string sql, string parameter, string value)
    {
        using var query = db.Database.CreateQuery(sql);
        query.Parameters = new Parameters().SetString(parameter, value);
        return query.Explain();
    }
}
