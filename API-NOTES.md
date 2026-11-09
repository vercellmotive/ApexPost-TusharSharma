# ApexPost.Api notes

The API exposes shipments and their scans over HTTPS, in two versions: `/api/v1/...` (the Part B contract, deprecated) and `/api/v2/...`. The table uses `v{n}` for either. [`VERSIONING.md`](VERSIONING.md) lists what differs. Couriers upload the scans they recorded offline in Part A, and the customer portal and partners track shipments. Every error response is ProblemDetails (`application/problem+json`) with a `traceId`.

## Endpoints

| Verb | Route | Auth | Purpose | Status codes |
|---|---|---|---|---|
| GET | `/api/v{n}/shipments?page=&pageSize=&status=&city=` | Any signed-in user | Paged, filtered list, newest first | 200 list; 400 page below 1, pageSize outside 1–50, unknown status; 401 no or invalid token; 403 wrong role; 429 rate limit |
| GET | `/api/v{n}/shipments/{trackingNumber}` | Any signed-in user | One shipment | 200; 404 unknown tracking number; 401 no or invalid token; 403 wrong role; 429 rate limit |
| POST | `/api/v{n}/shipments` | Admin | Create a shipment in Created status | 201 with `Location`; 400 validation; 409 duplicate tracking number; 415 not JSON; 401 no or invalid token; 403 wrong role; 429 rate limit |
| PUT | `/api/v{n}/shipments/{trackingNumber}` | Admin | Replace recipient, city and weight | 200; 400; 404; 409 shipment is Delivered or Failed, or changed by another request; 415; 401 no or invalid token; 403 wrong role; 429 rate limit |
| PATCH | `/api/v{n}/shipments/{trackingNumber}/status` | Courier | Change status only | 200; 400 missing or unknown status; 404; 409 move not allowed, or changed by another request; 415; 401 no or invalid token; 403 wrong role; 429 rate limit |
| DELETE | `/api/v{n}/shipments/{trackingNumber}` | Admin | Delete a shipment and its scans | 204; 404; 409 status is not Created; 401 no or invalid token; 403 wrong role; 429 rate limit |
| POST | `/api/v{n}/shipments/{trackingNumber}/scans` | Courier, own courierId only | Bulk upload of offline scans | 200 with counts; 400 validation; 403 a scan under another courier's id; 404; 415; 401 no or invalid token; 403 wrong role; 429 rate limit |
| GET | `/api/v{n}/shipments/{trackingNumber}/scans` | Any signed-in user | Scan history, oldest first | 200; 404; 401 no or invalid token; 403 wrong role; 429 rate limit |
| GET | `/api/v{n}/partner/shipments/{trackingNumber}` | Partner API key in `X-Api-Key` | Read-only status for a partner | 200; 404; 401 missing or wrong key; 429 more than 30 requests a minute |

The 401, 403 and 429 responses are ProblemDetails and apply to every route above; 403 applies only where a role is required. Role names are in `ApexPostRoles`; a token may hold both.
The Part B URLs without a version, such as `/api/shipments`, are rewritten to v1 and keep working. An unknown version such as `/api/v3/...` returns 404. Any route also returns 500 ProblemDetails from the global exception handler if something unexpected fails, and 405 for a verb the route does not have.

`{trackingNumber}` uses the `trackingNumber` route constraint. A value that is not `APX-` and eight digits does not match the route, so it gets 404 like any other unknown tracking number.

## Status rules

Created → PickedUp → InTransit → OutForDelivery → Delivered or Failed. Each status moves only to the next one. Delivered and Failed are final. The 409 response says which status the shipment can move to.

## Validation

[ApiController] validates every request DTO and returns `ValidationProblemDetails` with a 400. Error keys use the JSON names, such as `recipientPhone` or `scans[0].scannedAtUtc`.

| Field | Rule |
|---|---|
| `trackingNumber` | `^APX-\d{8}$`, with `\d` meaning only 0-9 (`[TrackingNumber]`) |
| `weightKg` | 0.1 to 70 |
| `recipientName`, `recipientPhone`, `recipientAddress`, `city` | Required, with length limits |
| `recipientPhone` | Custom `[PhoneNumber]`: optional `+`, digits grouped by spaces, hyphens or brackets, 8 to 15 digits |
| `originBranchCode` | Required. The service checks that the Week 3 branch exists and is open, and returns a 400 on this field if not |
| `scans[i].scannedAtUtc` | Custom `[NotInFuture]`, allowing 5 minutes of device clock drift |
| `scans[i].recipientSignatureName` | Custom `IValidatableObject` rule: a Delivered scan must name who signed |
| `scans[i].scanId` | `scan::` and a lower-case GUID, the Part A document id |
| `scans` | 1 to 100 per request |

## Idempotent scan upload

The scan id from the courier device is the primary key of `tracking.Scans`. An upload returns three counts:

- `accepted`: scans stored by this request.
- `duplicatesSkipped`: scans already stored for this shipment, or repeated in the same batch. Nothing is written for them, so a device can safely resend a batch whose reply it never received.
- `rejected`: scans refused, each listed in `rejections` with a reason. Rejected scans are: a scan id already recorded for a different shipment, a scan time before the shipment was created, and the service's own checks for a future time or an unsigned delivery.

If two uploads of the same scans race, the primary key refuses the second insert. The store then inserts that batch one scan at a time and counts the ones that were already there as duplicates. A test runs four uploads of one batch in parallel against SQL Server and finds each scan stored once.

## Scans and the shipment's status

A scan upload does not move a shipment along the status path, which is the job of `PATCH .../status`, with one exception: a Delivered or Failed scan closes a shipment that is `OutForDelivery`, moving it to Delivered or Failed, so the status and the scan history agree. A scan that arrives earlier in the journey changes nothing. If another request moved the shipment first, that change stands and the scans are still stored.

## Rate limits

429 ProblemDetails with `Retry-After` when a caller sends more than 600 requests a minute to any endpoint, or more than 30 to the partner route. See [`SECURITY.md`](SECURITY.md).

## Design

- **Thin controllers.** Each action binds a request DTO, maps it to a service command, calls `IShipmentService` and maps the result. The service returns `ServiceResult<T>`, and `ServiceErrorResults` turns a NotFound, Conflict or Validation error into ProblemDetails. Request DTOs are never bound to EF entities.
- **Reuse of Week 3.** `Program.cs` registers the Week 3 `ApexPostDbContext`, repositories and services with `AddSqlServerPersistence()`. The shipment service checks the origin branch through the Week 3 `IBranchRepository`, and the store reports duplicates and lost updates as the Week 3 `DuplicateValueException` and `ConcurrencyConflictException`.
- **Week 3 data layer unchanged.** Shipments with tracking numbers and courier scans are new data. They live in their own `tracking` schema in the same `ApexPostDb` database, with their own `TrackingDbContext` and migrations, so no Week 3 file or migration changes. The context uses the same `ApexPostDb` connection string.
- **Errors.** `GlobalExceptionHandler` is an `IExceptionHandler`. It logs the exception and returns 500 ProblemDetails with a trace id. Only in Development does it add the exception message and stack trace. `UseStatusCodePages` gives empty 404 and 405 responses a ProblemDetails body too.
- **Concurrency.** `tracking.Shipments` has a `rowversion` column, so a lost update returns 409 instead of silently overwriting another change.
