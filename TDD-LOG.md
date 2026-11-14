# TDD log: scans recorded after a shipment closed

One new business rule was built test-first, in three commits labelled `RED:`, `GREEN:` and `REFACTOR:` on `week5-testing`. They keep those labels in `master` because the branch is merged with a merge commit, not squashed.

| Step | Commit | Result of `dotnet test ApexPost.slnx` |
|---|---|---|
| RED | `2d72146` RED: a scan recorded after its shipment was closed must be rejected | 4 of the 7 new cases fail, everything else passes |
| GREEN | `24cbdb7` GREEN: reject a scan recorded after its shipment was closed | Unit 298 passed, integration 315 passed and 36 skipped (SQL Server) |
| REFACTOR | `b9aa7ba` REFACTOR: move the closed-shipment rule into the Shipment domain object | The same counts. No test was edited. |

## The requirement

Risk R8 in `TESTING-STRATEGY.md`: devices upload late, sometimes hours after the parcel was delivered. Before this change the server stored any scan for any shipment, so a scan uploaded after a shipment was Delivered could make the history show the parcel moving again.

> Once a shipment is **Delivered** or **Failed** it is closed. A scan recorded **after** the moment it closed is rejected with a reason and not stored. A scan recorded **up to** that moment still counts, because a courier who was offline uploads earlier scans late.

The rule did not exist before: `ShipmentService.UploadScansAsync` accepted the scan, and nothing in the domain knew when a shipment had closed.

## The first failing test

`ApexPost.UnitTests/Tracking/ClosedShipmentScanTests.cs`, written before any production change. The store is a Moq mock and the clock is a `FakeTimeProvider`, so the test fixes the closing time and the server time exactly:

```csharp
[Theory]
[InlineData(ShipmentStatus.Delivered)]
[InlineData(ShipmentStatus.Failed)]
public async Task UploadScansAsync_ScanRecordedAfterShipmentClosed_IsRejectedWithReason(ShipmentStatus finalStatus)
{
    // Arrange
    GivenShipmentClosedAs(finalStatus);
    var late = Make.Scan(type: ScanType.InTransit, at: ClosedAt.AddMinutes(15));

    // Act
    var result = await _service.UploadScansAsync("APX-10000001", [late], Make.CourierId);

    // Assert
    Assert.Equal(0, result.Value.Accepted);
    var rejection = Assert.Single(result.Value.Rejections);
    Assert.Equal($"The shipment was closed as {finalStatus} before this scan was recorded.", rejection.Reason);
    _store.Verify(store => store.AddScansAsync(It.IsAny<IReadOnlyList<TrackingScan>>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

The same commit added a boundary theory (one hour before, exactly at, one second after and one hour after the close) and a test that an open shipment still accepts a scan of any time. The run at the RED commit:

```text
UploadScansAsync_ScanRecordedAfterShipmentClosed_IsRejectedWithReason(finalStatus: Delivered) [FAIL]
UploadScansAsync_ScanRecordedAfterShipmentClosed_IsRejectedWithReason(finalStatus: Failed) [FAIL]
UploadScansAsync_ScanTimeAroundTheClosingTime_AcceptedOnlyUpToTheClose(secondsAfterClose: 1, accepted: False) [FAIL]
UploadScansAsync_ScanTimeAroundTheClosingTime_AcceptedOnlyUpToTheClose(secondsAfterClose: 3600, accepted: False) [FAIL]
Expected: 0
Actual:   1
Failed!  - Failed: 4, Passed: 3, Total: 7
```

The three cases that already passed are the ones the rule must not break: an earlier scan, a scan at the exact closing moment, and an open shipment.

## The minimal implementation (GREEN)

One more check at the end of the service's existing rejection rules:

```csharp
if (shipment.IsClosed && scan.ScannedAtUtc > shipment.UpdatedAtUtc)
{
    return $"The shipment was closed as {shipment.Status} before this scan was recorded.";
}
```

It works because a closed shipment never changes again, so its last update is the moment it closed. That knowledge was left implicit on purpose, to get to green with the smallest change.

## What was refactored

- **The rule moved into the domain.** `Shipment.ClosedAtUtc` names the moment the shipment closed (null while open), and `Shipment.AcceptsScanRecordedAt(time)` owns the comparison. The service now asks the shipment instead of reading `UpdatedAtUtc`, which made the rule depend on a field that sounds unrelated.
- **The rejection texts moved into one place**, `ScanRejectionReasons`, instead of five string literals spread through the service.
- `TrackingDbContext` ignores `ClosedAtUtc` explicitly, so it can never become a column by accident.

The tests were not touched in the REFACTOR commit, and all of them stayed green. Domain-level tests for the two new members (`ShipmentClosingTests`) were added in the next ordinary commit, alongside this log.

## Production code changed to make it testable

| Change | Why |
|---|---|
| None was needed in `ShipmentService` itself. | It already takes `IShipmentStore`, `IBranchRepository` and `TimeProvider` through its constructor (Week 4), so a Moq store and a `FakeTimeProvider` could replace them without touching the code under test. That is why the first test could be written before any production change. |
| `Shipment.ClosedAtUtc` and `AcceptsScanRecordedAt` (REFACTOR) | The rule became testable on a plain domain object, with no store and no service. |
| Elsewhere in Week 5: `TransactionService` now takes a `TimeProvider` instead of calling `DateTimeOffset.UtcNow` | The Week 3 counter tests depended on the real time of day: a sale is refused outside opening hours and dated by the clock. With the clock injected, the test factory fixes the time. |
| Elsewhere in Week 5: `PhoneNumberAttribute.Shape()` changed from `private` to `internal` | A test checks the regular expression's options (no backtracking) instead of timing it with a stopwatch, which would depend on the machine. |

## How the rule is covered now

| Level | Test |
|---|---|
| Service, with Moq | `ClosedShipmentScanTests` (written first) |
| Domain | `ShipmentClosingTests` |
| Acceptance | `ScanSync.feature`, scenario "A scan recorded after delivery is rejected" |
