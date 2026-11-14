using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using ApexPost.Testing.Tracking;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Tracking;

public sealed class ShipmentServiceTests
{
    private readonly InMemoryShipmentStore _store = new();
    private readonly FakeTimeProvider _clock = TestTime.NewClock();
    private readonly ShipmentService _service;

    public ShipmentServiceTests() => _service = new ShipmentService(_store, new FakeBranches(), _clock);

    [Fact]
    public async Task CreateAsync_ValidRequest_StoresShipmentInCreatedStatus()
    {
        // Act
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(ShipmentStatus.Created, result.Value.Status);
        Assert.Equal(_clock.GetUtcNow(), result.Value.CreatedAtUtc);
        Assert.True(await _store.ExistsAsync("APX-20000001"));
    }

    [Theory]
    [InlineData("B999", "does not exist")]
    [InlineData("B003", "is closed")]
    public async Task CreateAsync_UnknownOrClosedBranch_ReturnsValidationError(string branch, string reason)
    {
        // Act
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", branch, Make.Recipient(), 1.5m));

        // Assert
        Assert.Equal(ServiceErrorKind.Validation, result.Error?.Kind);
        Assert.Equal(nameof(NewShipment.OriginBranchCode), result.Error?.Field);
        Assert.Contains(reason, result.Error?.Detail);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task CreateAsync_ExistingTrackingNumber_ReturnsConflict()
    {
        // Arrange
        _store.Seed(Make.Shipment("APX-20000001"));

        // Act
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Duplicate tracking number", result.Error?.Title);
    }

    [Fact]
    public async Task CreateAsync_DuplicateWinsRace_ReturnsConflict()
    {
        // Arrange
        _store.FailNextSave = new DuplicateValueException("The unique index refused the row.");

        // Act
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task EveryUseCase_UnknownShipment_ReturnsNotFound()
    {
        // Arrange
        const string missing = "APX-99999999";
        var details = new ShipmentDetails(Make.Recipient(), 1m);

        // Act and assert
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.GetAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.ReplaceDetailsAsync(missing, details)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.ChangeStatusAsync(missing, ShipmentStatus.PickedUp)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.DeleteAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.GetScansAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.UploadScansAsync(missing, [Make.Scan()], Make.CourierId)).Error?.Kind);
    }

    [Fact]
    public async Task ChangeStatusAsync_NextStatus_MovesAndStampsTime()
    {
        // Arrange
        _store.Seed(Make.Shipment());
        _clock.SetUtcNow(TestTime.Start.AddHours(2));

        // Act
        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.PickedUp);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(ShipmentStatus.PickedUp, result.Value.Status);
        Assert.Equal(_clock.GetUtcNow(), result.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task ChangeStatusAsync_InvalidTransition_ReturnsConflictNamingAllowedStatus()
    {
        // Arrange
        _store.Seed(Make.Shipment());

        // Act
        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.Delivered);

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Contains("can move to PickedUp", result.Error?.Detail);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task ChangeStatusAsync_LostUpdate_ReturnsConflict()
    {
        // Arrange
        _store.Seed(Make.Shipment());
        _store.FailNextSave = new ConcurrencyConflictException("Row version changed.");

        // Act
        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.PickedUp);

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Shipment changed", result.Error?.Title);
    }

    [Fact]
    public async Task ReplaceDetailsAsync_ClosedShipment_ReturnsConflict()
    {
        // Arrange
        _store.Seed(Make.ShipmentIn(ShipmentStatus.Delivered));

        // Act
        var result = await _service.ReplaceDetailsAsync("APX-10000001", new ShipmentDetails(Make.Recipient("Perth"), 1m));

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task DeleteAsync_CreatedOrLater_RemovesOnlyCreated()
    {
        // Arrange
        _store.Seed(Make.Shipment("APX-10000001"));
        _store.Seed(Make.ShipmentIn(ShipmentStatus.PickedUp, "APX-10000002"));

        // Act and assert
        Assert.True((await _service.DeleteAsync("APX-10000001")).Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, (await _service.DeleteAsync("APX-10000002")).Error?.Kind);
        Assert.False(await _store.ExistsAsync("APX-10000001"));
        Assert.True(await _store.ExistsAsync("APX-10000002"));
    }

    [Fact]
    public async Task UploadScansAsync_SameBatchTwice_StoresEachScanOnce()
    {
        // Arrange
        _store.Seed(Make.Shipment());
        IncomingScan[] batch = [Make.Scan(), Make.Scan(type: ScanType.InTransit)];

        var first = await _service.UploadScansAsync("APX-10000001", batch, Make.CourierId);

        // Act
        var second = await _service.UploadScansAsync("APX-10000001", batch, Make.CourierId);

        // Assert
        Assert.Equal((2, 0, 0), (first.Value!.Accepted, first.Value.DuplicatesSkipped, first.Value.Rejected));
        Assert.Equal((0, 2, 0), (second.Value!.Accepted, second.Value.DuplicatesSkipped, second.Value.Rejected));
        Assert.Equal(2, _store.AllScans.Count);
    }

    [Fact]
    public async Task UploadScansAsync_ScanRepeatedInBatch_CountsDuplicate()
    {
        // Arrange
        _store.Seed(Make.Shipment());
        var scan = Make.Scan();

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan, scan], Make.CourierId);

        // Assert
        Assert.Equal((1, 1), (result.Value!.Accepted, result.Value.DuplicatesSkipped));
    }

    [Fact]
    public async Task UploadScansAsync_ScanStoredByRacingUpload_CountsDuplicate()
    {
        // Arrange
        var shipment = _store.Seed(Make.Shipment());
        var scan = Make.Scan();
        ScanId.TryParse(scan.ScanId, out var id);
        _store.RaceScans.Add(TrackingScan.Record(id, shipment.Id, scan.ScanType, scan.CourierId, scan.Location, scan.ScannedAtUtc, null, null, _clock.GetUtcNow()));

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan, Make.Scan()], Make.CourierId);

        // Assert
        Assert.Equal((1, 1), (result.Value!.Accepted, result.Value.DuplicatesSkipped));
        Assert.Equal(2, _store.AllScans.Count);
    }

    [Fact]
    public async Task UploadScansAsync_InvalidScans_RejectsWithReasonAndStoresRest()
    {
        // Arrange
        _store.Seed(Make.Shipment("APX-10000001"));
        var other = _store.Seed(Make.Shipment("APX-10000002"));
        var taken = Make.Scan();
        ScanId.TryParse(taken.ScanId, out var takenId);
        _store.SeedScan(TrackingScan.Record(takenId, other.Id, ScanType.Pickup, "CR-1001", "Depot", TestTime.Start, null, null, TestTime.Start));

        var future = Make.Scan(at: _clock.GetUtcNow().AddHours(1));
        var beforeCreation = Make.Scan(at: TestTime.Start.AddHours(-1));
        var unsigned = Make.Scan(type: ScanType.Delivered);
        var badId = Make.Scan(scanId: "scan::not-a-guid");
        var good = Make.Scan();

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [taken, future, beforeCreation, unsigned, badId, good], Make.CourierId);

        // Assert
        Assert.Equal(1, result.Value!.Accepted);
        Assert.Equal(5, result.Value.Rejected);

        // Act
        var reasons = result.Value.Rejections.ToDictionary(rejection => rejection.ScanId, rejection => rejection.Reason);

        // Assert
        Assert.Contains("another shipment", reasons[taken.ScanId]);
        Assert.Contains("future", reasons[future.ScanId]);
        Assert.Contains("before the shipment was created", reasons[beforeCreation.ScanId]);
        Assert.Contains("signed", reasons[unsigned.ScanId]);
        Assert.Contains("scan::", reasons[badId.ScanId]);
    }

    [Fact]
    public async Task UploadScansAsync_ClockDriftWithinTolerance_IsAccepted()
    {
        // Arrange
        _store.Seed(Make.Shipment());

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [
            Make.Scan(at: _clock.GetUtcNow() + ShipmentService.ScanClockTolerance),
            Make.Scan(at: TestTime.Start - ShipmentService.ScanClockTolerance),
        ], Make.CourierId);

        // Assert
        Assert.Equal(2, result.Value!.Accepted);
    }

    [Fact]
    public async Task GetAsync_ShipmentWithScans_CarriesLatestScan()
    {
        // Arrange
        _store.Seed(Make.Shipment());
        await _service.UploadScansAsync("APX-10000001", [
            Make.Scan(type: ScanType.Pickup, at: TestTime.Start.AddMinutes(10)),
            Make.Scan(type: ScanType.InTransit, at: TestTime.Start.AddMinutes(40)),
        ], Make.CourierId);

        // Act
        var view = (await _service.GetAsync("APX-10000001")).Value!;

        // Assert
        Assert.Equal(ScanType.InTransit, view.LatestScan?.ScanType);
        Assert.Equal(TestTime.Start.AddMinutes(40), view.LatestScan?.ScannedAtUtc);
    }

    [Theory]
    [InlineData(ScanType.Delivered, ShipmentStatus.Delivered)]
    [InlineData(ScanType.Failed, ShipmentStatus.Failed)]
    public async Task UploadScansAsync_DeliveredOrFailedScanWhileOutForDelivery_ClosesShipment(ScanType scanType, ShipmentStatus expected)
    {
        // Arrange
        _store.Seed(Make.ShipmentIn(ShipmentStatus.OutForDelivery));

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [Make.Scan(type: scanType, signature: "Mia Fictional")], Make.CourierId);

        // Assert
        Assert.Equal(1, result.Value!.Accepted);
        Assert.Equal(expected, (await _service.GetAsync("APX-10000001")).Value!.Status);
    }

    [Theory]
    [InlineData(ShipmentStatus.Created)]
    [InlineData(ShipmentStatus.PickedUp)]
    [InlineData(ShipmentStatus.InTransit)]
    public async Task UploadScansAsync_DeliveredScanBeforeOutForDelivery_LeavesStatus(ShipmentStatus status)
    {
        // Arrange
        _store.Seed(Make.ShipmentIn(status));

        // Act
        await _service.UploadScansAsync("APX-10000001", [Make.Scan(type: ScanType.Delivered, signature: "Mia Fictional")], Make.CourierId);

        // Assert
        Assert.Equal(status, (await _service.GetAsync("APX-10000001")).Value!.Status);
    }

    [Fact]
    public async Task UploadScansAsync_OtherTypesOrLostRace_LeavesStatusWithoutError()
    {
        // Arrange
        _store.Seed(Make.ShipmentIn(ShipmentStatus.OutForDelivery));

        // Act
        var transit = await _service.UploadScansAsync("APX-10000001", [Make.Scan(type: ScanType.InTransit)], Make.CourierId);

        // Assert
        Assert.Equal(ShipmentStatus.OutForDelivery, (await _service.GetAsync("APX-10000001")).Value!.Status);
        Assert.Equal(1, transit.Value!.Accepted);

        // Arrange (next step)
        _store.FailNextSave = new ConcurrencyConflictException("Another request moved it.");

        // Act
        var delivered = await _service.UploadScansAsync("APX-10000001", [Make.Scan(type: ScanType.Delivered, signature: "Mia Fictional")], Make.CourierId);

        // Assert
        Assert.Equal(1, delivered.Value!.Accepted);
    }

    [Fact]
    public async Task ListAsync_PageSizeAboveMaximum_Throws()
    {
        // Act and assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ListAsync(new ShipmentQuery(1, ShipmentQuery.MaximumPageSize + 1, null, null)));
    }
}
