using ApexPost.Application.Interfaces;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace ApexPost.UnitTests.Tracking;

/// <summary>
/// <see cref="ShipmentService"/> isolated with Moq. These tests check interactions the state-based tests in
/// <see cref="ShipmentServiceTests"/> cannot see directly: what the service asks the store to do, and what it must
/// never ask it to do. A mock proves the service's decisions, not the store: whether the query, the unique index or
/// the transaction work is left to the SQLite and SQL Server tests.
/// </summary>
public sealed class ShipmentServiceMockTests
{
    private readonly Mock<IShipmentStore> _store = new(MockBehavior.Strict);
    private readonly Mock<IBranchRepository> _branches = new();
    private readonly FakeTimeProvider _clock = TestTime.NewClock();
    private readonly ShipmentService _service;

    public ShipmentServiceMockTests()
    {
        _branches
            .Setup(branches => branches.GetByCodeAsync(It.Is<BranchCode>(code => code.Value == "B001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Branch("B001", isActive: true));
        _service = new ShipmentService(_store.Object, _branches.Object, _clock);
    }

    private static Branch Branch(string code, bool isActive) => new()
    {
        Id = new BranchCode(code),
        Name = $"Branch {code}",
        Region = "NSW",
        OpenTime = TimeSpan.FromHours(9),
        CloseTime = TimeSpan.FromHours(17),
        IsActive = isActive,
    };

    private static NewShipment Request(string trackingNumber = "APX-20000001", string branch = "B001") =>
        new(trackingNumber, branch, Make.Recipient(), 1.5m);

    [Fact]
    public async Task CreateAsync_NewTrackingNumber_AddsOneCreatedShipmentAndSavesOnce()
    {
        // Arrange
        _store.Setup(store => store.ExistsAsync("APX-20000001", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _store.Setup(store => store.Add(It.IsAny<Shipment>()));
        _store.Setup(store => store.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAsync(Request());

        // Assert
        Assert.True(result.Succeeded);
        _store.Verify(store => store.Add(It.Is<Shipment>(shipment =>
            shipment.TrackingNumber == "APX-20000001"
            && shipment.Status == ShipmentStatus.Created
            && shipment.CreatedAtUtc == _clock.GetUtcNow())), Times.Once);
        _store.Verify(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ExistingTrackingNumber_ReturnsConflictAndNeverAddsOrSaves()
    {
        // Arrange
        _store.Setup(store => store.ExistsAsync(It.Is<string>(number => number == "APX-20000001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.CreateAsync(Request());

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Duplicate tracking number", result.Error?.Title);
        _store.Verify(store => store.Add(It.IsAny<Shipment>()), Times.Never);
        _store.Verify(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_DuplicateRejectedByTheDatabase_ReturnsConflict()
    {
        // Arrange
        // Another request inserted the same number between the check and the save; the unique index refuses it.
        _store.Setup(store => store.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _store.Setup(store => store.Add(It.IsAny<Shipment>()));
        _store.Setup(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateValueException("The unique index refused the row."));

        // Act
        var result = await _service.CreateAsync(Request());

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
    }

    [Theory]
    [InlineData("B999")]
    [InlineData("B003")]
    public async Task CreateAsync_UnknownOrClosedBranch_ReturnsValidationErrorWithoutTouchingTheStore(string branch)
    {
        // Arrange
        _branches.Setup(branches => branches.GetByCodeAsync(It.Is<BranchCode>(code => code.Value == "B003"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Branch("B003", isActive: false));

        // Act
        var result = await _service.CreateAsync(Request(branch: branch));

        // Assert
        Assert.Equal(ServiceErrorKind.Validation, result.Error?.Kind);
        Assert.Equal(nameof(NewShipment.OriginBranchCode), result.Error?.Field);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangeStatusAsync_DeliveredToInTransit_ReturnsConflictAndNeverSaves()
    {
        // Arrange
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Make.ShipmentIn(ShipmentStatus.Delivered));

        // Act
        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.InTransit);

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Contains("nothing, because it is final", result.Error?.Detail);
        _store.Verify(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeStatusAsync_RowChangedByAnotherRequest_ReturnsConflict()
    {
        // Arrange
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(Make.Shipment());
        _store.Setup(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("Lost update."));

        // Act
        var result = await _service.ChangeStatusAsync("APX-10000001", ShipmentStatus.PickedUp);

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        Assert.Equal("Shipment changed", result.Error?.Title);
    }

    [Fact]
    public async Task DeleteAsync_ShipmentAlreadyPickedUp_ReturnsConflictAndNeverRemoves()
    {
        // Arrange
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Make.ShipmentIn(ShipmentStatus.PickedUp));

        // Act
        var result = await _service.DeleteAsync("APX-10000001");

        // Assert
        Assert.Equal(ServiceErrorKind.Conflict, result.Error?.Kind);
        _store.Verify(store => store.Remove(It.IsAny<Shipment>()), Times.Never);
        _store.Verify(store => store.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadScansAsync_ScanUnderAnotherCourierId_ReturnsForbiddenWithoutReadingTheStore()
    {
        // Arrange
        var scans = new[] { Make.Scan() with { CourierId = "CR-2002" } };

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", scans, uploadingCourierId: Make.CourierId);

        // Assert
        Assert.Equal(ServiceErrorKind.Forbidden, result.Error?.Kind);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadScansAsync_ScanIdAlreadyStoredForThisShipment_CountsDuplicateAndAddsNothing()
    {
        // Arrange
        var shipment = Make.Shipment();
        var scan = Make.Scan();
        ScanId.TryParse(scan.ScanId, out var id);
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(shipment);
        _store.Setup(store => store.FindScanShipmentsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [id] = shipment.Id });

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan], Make.CourierId);

        // Assert
        Assert.Equal(0, result.Value!.Accepted);
        Assert.Equal(1, result.Value.DuplicatesSkipped);
        Assert.Empty(result.Value.Rejections);
        _store.Verify(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadScansAsync_ScanIdOwnedByAnotherShipment_RejectsItWithAReason()
    {
        // Arrange
        var scan = Make.Scan();
        ScanId.TryParse(scan.ScanId, out var id);
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(Make.Shipment());
        _store.Setup(store => store.FindScanShipmentsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [id] = 999 });

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [scan], Make.CourierId);

        // Assert
        var rejection = Assert.Single(result.Value!.Rejections);
        Assert.Equal(scan.ScanId, rejection.ScanId);
        Assert.Contains("another shipment", rejection.Reason);
        _store.Verify(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadScansAsync_TwoNewScansAndOneRepeat_AddsTheTwoInOneCall()
    {
        // Arrange
        var first = Make.Scan();
        var second = Make.Scan(type: ScanType.InTransit);
        _store.Setup(store => store.FindAsync("APX-10000001", It.IsAny<CancellationToken>())).ReturnsAsync(Make.Shipment());
        _store.Setup(store => store.FindScanShipmentsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        _store.Setup(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<TrackingScan> scans, CancellationToken _) => scans.Select(scan => scan.ScanId).ToHashSet());

        // Act
        var result = await _service.UploadScansAsync("APX-10000001", [first, second, first], Make.CourierId);

        // Assert
        Assert.Equal(2, result.Value!.Accepted);
        Assert.Equal(1, result.Value.DuplicatesSkipped);
        _store.Verify(store => store.AddScansAsync(
            It.Is<IReadOnlyList<TrackingScan>>(scans => scans.Count == 2 && scans.All(scan => scan.ReceivedAtUtc == _clock.GetUtcNow())),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
