using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Tests;

public sealed class ShipmentServiceTests
{
    private readonly InMemoryShipmentStore _store = new();
    private readonly TestClock _clock = new();
    private readonly ShipmentService _service;

    public ShipmentServiceTests() => _service = new ShipmentService(_store, new FakeBranches(), _clock);

    [Fact]
    public async Task Create_stores_a_shipment_in_created_status()
    {
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        Assert.True(result.Succeeded);
        Assert.Equal(ShipmentStatus.Created, result.Value.Status);
        Assert.Equal(_clock.Now, result.Value.CreatedAtUtc);
        Assert.True(await _store.ExistsAsync("APX-20000001"));
    }

    [Theory]
    [InlineData("B999", "does not exist")]
    [InlineData("B003", "is closed")]
    public async Task Create_needs_an_open_week_3_branch(string branch, string reason)
    {
        var result = await _service.CreateAsync(new NewShipment("APX-20000001", branch, Make.Recipient(), 1.5m));

        Assert.Equal(ServiceErrorKind.Validation, result.Error?.Kind);
        Assert.Equal(nameof(NewShipment.OriginBranchCode), result.Error?.Field);
        Assert.Contains(reason, result.Error?.Detail);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Create_reports_a_duplicate_tracking_number_as_a_conflict()
    {
        _store.Seed(Make.Shipment("APX-20000001"));

        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Duplicate tracking number", result.Error?.Title);
    }

    [Fact]
    public async Task Create_reports_a_duplicate_that_wins_the_race_as_a_conflict()
    {
        _store.FailNextSave = new DuplicateValueException("The unique index refused the row.");

        var result = await _service.CreateAsync(new NewShipment("APX-20000001", "B001", Make.Recipient(), 1.5m));

        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task Missing_shipments_are_not_found_by_every_use_case()
    {
        const string missing = "APX-99999999";
        var details = new ShipmentDetails(Make.Recipient(), 1m);

        Assert.Equal(ServiceErrorKind.NotFound, (await _service.GetAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.ReplaceDetailsAsync(missing, details)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.ChangeStatusAsync(missing, ShipmentStatus.PickedUp)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.DeleteAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.GetScansAsync(missing)).Error?.Kind);
        Assert.Equal(ServiceErrorKind.NotFound, (await _service.UploadScansAsync(missing, [Make.Scan()], Make.CourierId)).Error?.Kind);
    }

    [Fact]
    public async Task ChangeStatus_moves_along_the_path_and_stamps_the_time()
    {
        _store.Seed(Make.Shipment());
        _clock.Now = TestClock.Start.AddHours(2);

        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.PickedUp);

        Assert.True(result.Succeeded);
        Assert.Equal(ShipmentStatus.PickedUp, result.Value.Status);
        Assert.Equal(_clock.Now, result.Value.UpdatedAtUtc);
    }

    [Fact]
    public async Task ChangeStatus_refuses_an_invalid_transition_and_names_the_allowed_one()
    {
        _store.Seed(Make.Shipment());

        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.Delivered);

        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Contains("can move to PickedUp", result.Error?.Detail);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task ChangeStatus_reports_a_lost_update_as_a_conflict()
    {
        _store.Seed(Make.Shipment());
        _store.FailNextSave = new ConcurrencyConflictException("Row version changed.");

        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.PickedUp);

        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Shipment changed", result.Error?.Title);
    }

    [Fact]
    public async Task ReplaceDetails_refuses_a_closed_shipment()
    {
        _store.Seed(Make.ShipmentIn(ShipmentStatus.Delivered));

        var result = await _service.ReplaceDetailsAsync("APX-10000001", new ShipmentDetails(Make.Recipient("Perth"), 1m));

        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
    }

    [Fact]
    public async Task Delete_removes_only_a_created_shipment()
    {
        _store.Seed(Make.Shipment("APX-10000001"));
        _store.Seed(Make.ShipmentIn(ShipmentStatus.PickedUp, "APX-10000002"));

        Assert.True((await _service.DeleteAsync("APX-10000001")).Succeeded);
        Assert.Equal(ServiceErrorKind.Conflict, (await _service.DeleteAsync("APX-10000002")).Error?.Kind);
        Assert.False(await _store.ExistsAsync("APX-10000001"));
        Assert.True(await _store.ExistsAsync("APX-10000002"));
    }

    [Fact]
    public async Task Uploading_the_same_batch_twice_stores_each_scan_once()
    {
        _store.Seed(Make.Shipment());
        IncomingScan[] batch = [Make.Scan(), Make.Scan(type: ScanType.InTransit)];

        var first = await _service.UploadScansAsync("APX-10000001", batch, Make.CourierId);
        var second = await _service.UploadScansAsync("APX-10000001", batch, Make.CourierId);

        Assert.Equal((2, 0, 0), (first.Value!.Accepted, first.Value.DuplicatesSkipped, first.Value.Rejected));
        Assert.Equal((0, 2, 0), (second.Value!.Accepted, second.Value.DuplicatesSkipped, second.Value.Rejected));
        Assert.Equal(2, _store.AllScans.Count);
    }

    [Fact]
    public async Task A_scan_repeated_inside_one_batch_is_a_duplicate()
    {
        _store.Seed(Make.Shipment());
        var scan = Make.Scan();

        var result = await _service.UploadScansAsync("APX-10000001", [scan, scan], Make.CourierId);

        Assert.Equal((1, 1), (result.Value!.Accepted, result.Value.DuplicatesSkipped));
    }

    [Fact]
    public async Task A_scan_another_upload_stored_during_this_one_counts_as_a_duplicate()
    {
        var shipment = _store.Seed(Make.Shipment());
        var scan = Make.Scan();
        ScanId.TryParse(scan.ScanId, out var id);
        _store.RaceScans.Add(TrackingScan.Record(id, shipment.Id, scan.ScanType, scan.CourierId, scan.Location, scan.ScannedAtUtc, null, null, _clock.Now));

        var result = await _service.UploadScansAsync("APX-10000001", [scan, Make.Scan()], Make.CourierId);

        Assert.Equal((1, 1), (result.Value!.Accepted, result.Value.DuplicatesSkipped));
        Assert.Equal(2, _store.AllScans.Count);
    }

    [Fact]
    public async Task Rejected_scans_carry_a_reason_and_do_not_stop_the_rest()
    {
        _store.Seed(Make.Shipment("APX-10000001"));
        var other = _store.Seed(Make.Shipment("APX-10000002"));
        var taken = Make.Scan();
        ScanId.TryParse(taken.ScanId, out var takenId);
        _store.SeedScan(TrackingScan.Record(takenId, other.Id, ScanType.Pickup, "CR-1001", "Depot", TestClock.Start, null, null, TestClock.Start));

        var future = Make.Scan(at: _clock.Now.AddHours(1));
        var beforeCreation = Make.Scan(at: TestClock.Start.AddHours(-1));
        var unsigned = Make.Scan(type: ScanType.Delivered);
        var badId = Make.Scan(scanId: "scan::not-a-guid");
        var good = Make.Scan();

        var result = await _service.UploadScansAsync("APX-10000001", [taken, future, beforeCreation, unsigned, badId, good], Make.CourierId);

        Assert.Equal(1, result.Value!.Accepted);
        Assert.Equal(5, result.Value.Rejected);
        var reasons = result.Value.Rejections.ToDictionary(rejection => rejection.ScanId, rejection => rejection.Reason);
        Assert.Contains("another shipment", reasons[taken.ScanId]);
        Assert.Contains("future", reasons[future.ScanId]);
        Assert.Contains("before the shipment was created", reasons[beforeCreation.ScanId]);
        Assert.Contains("signed", reasons[unsigned.ScanId]);
        Assert.Contains("scan::", reasons[badId.ScanId]);
    }

    [Fact]
    public async Task Clock_drift_within_the_tolerance_is_accepted()
    {
        _store.Seed(Make.Shipment());

        var result = await _service.UploadScansAsync("APX-10000001", [
            Make.Scan(at: _clock.Now + ShipmentService.ScanClockTolerance),
            Make.Scan(at: TestClock.Start - ShipmentService.ScanClockTolerance),
        ], Make.CourierId);

        Assert.Equal(2, result.Value!.Accepted);
    }

    [Fact]
    public async Task The_shipment_view_carries_its_latest_scan()
    {
        _store.Seed(Make.Shipment());
        await _service.UploadScansAsync("APX-10000001", [
            Make.Scan(type: ScanType.Pickup, at: TestClock.Start.AddMinutes(10)),
            Make.Scan(type: ScanType.InTransit, at: TestClock.Start.AddMinutes(40)),
        ], Make.CourierId);

        var view = (await _service.GetAsync("APX-10000001")).Value!;

        Assert.Equal(ScanType.InTransit, view.LatestScan?.ScanType);
        Assert.Equal(TestClock.Start.AddMinutes(40), view.LatestScan?.ScannedAtUtc);
    }

    [Fact]
    public async Task List_refuses_a_page_size_above_the_maximum()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.ListAsync(new ShipmentQuery(1, ShipmentQuery.MaximumPageSize + 1, null, null)));
    }
}
