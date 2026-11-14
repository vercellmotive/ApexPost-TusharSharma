# ApexPost testing strategy

This is the plan for proving that ApexPost is reliable and safe to change before the next release. It was written before any Week 5 test was added or changed (it is the first commit on `week5-testing`), and the sections at the end were completed after the full run.

The aim is not the largest number of tests. Each test exists because it protects one of the risks below, at the cheapest level that can actually catch the failure.

## 1. Risk map

Likelihood is how easily a change could break the behaviour. Impact is what it costs when it does. The last column is where the strongest test for that risk lives.

| # | Risk | Likelihood | Impact | Why it is costly | Main test level |
|---|---|---|---|---|---|
| R1 | **Shipment status transitions** go wrong (a skipped step, a move out of Delivered, a move back to InTransit) | Medium: the rule is a table that is easy to edit | High: customers and partners see a false status; a closed delivery reopens | Tracking is the product | Unit (every one of the 36 from/to pairs), plus one API test for the 409 |
| R2 | **Duplicate tracking numbers** are stored | Medium: two requests can race between the check and the insert | High: two parcels share one label, and scans land on the wrong one | Mis-delivery | Unit (service decision, verified with Moq), BDD at API level, SQL Server unique-index race (opt-in) |
| R3 | **Scan de-duplication** fails, so an offline batch that is re-sent stores scans twice | High: devices re-send whenever a reply is lost | High: history shows phantom movements; delivery counts are wrong | The idempotency contract between Part A and Part B | Unit (service), SQLite (store), BDD (device outbox to server), SQL Server parallel upload (opt-in) |
| R4 | **JWT role rules** are loosened (a Courier can delete, anyone can upload under another courierId) | Medium: one missing attribute or policy | Very high: a security incident | Data integrity and trust | API integration with real JWT validation, BDD access-control outline |
| R5 | **Client retry behaviour** is wrong: retrying a 4xx or a non-idempotent POST, or not retrying a 5xx | Medium: resilience settings are easy to change | High: duplicate writes, drained batteries, or scans that never leave the device | Couriers lose work | Unit with a fake `HttpMessageHandler`; resilience pipeline tests |
| R6 | **The offline outbox loses or wrongly marks scans** (Synced when the upload failed, Failed when the server was only unreachable) | Medium | Very high: a delivery proof disappears | Proof of delivery is a legal record | Unit with Moq (`IScanRepository`, `IShipmentUploader`), Couchbase Lite integration |
| R7 | **Validation rules** drift (tracking number format, 0.1–70 kg, phone format, future scan time, Delivered needs a signature) | High: attributes are edited often | Medium: bad data enters, or good data is refused at the counter | Rework and support calls | Unit theories with boundary values, API 400 tests, BDD outline for weight |
| R8 | **Scans arrive for a shipment that is already closed** and rewrite its story | Medium: devices upload late | High: a delivered parcel appears to move again | Disputes with customers | New rule built test-first (Task 4) |
| R9 | **Week 3 money and stock rules** break (overselling the last unit, money orders above the ID threshold) | Low: stable code | High: money and stock errors | Financial loss | Existing unit tests (kept), SQL Server tests (opt-in) |

R1 to R8 are the Week 4 flow that the next release changes. R9 is protected by the Week 3 tests, which are kept.

## 2. Test pyramid for ApexPost

| Level | Project | What it covers | Tools | Target share of test cases |
|---|---|---|---|---|
| Unit | `ApexPost.UnitTests` | Domain rules (status table, weight and tracking number boundaries, scan ids), application services with their dependencies mocked, validation attributes, the typed API client against a fake `HttpMessageHandler`, the outbox with a mocked repository and uploader, Week 3 domain and service rules | xUnit, Moq, `FakeTimeProvider` | about 65 % |
| Integration | `ApexPost.IntegrationTests` | Repositories against a real database engine (SQLite in-memory always, SQL Server when configured), Couchbase Lite repository, queries, indexes and listener against a real embedded database, API endpoints end to end with `WebApplicationFactory`, the resilience pipeline, the Week 3 MVC pipeline | xUnit, `WebApplicationFactory`, SQLite, Couchbase Lite, SQL Server (opt-in) | about 33 % |
| Acceptance (BDD) | `ApexPost.Specs` | A few business scenarios written in Gherkin with the business: shipment creation, offline scan sync, access control | Reqnroll on xUnit | about 2 % (a small number of high-value scenarios) |

Unit tests are many because they are fast (milliseconds) and point straight at the broken rule. Integration tests are fewer and prove what a unit test cannot: that the SQL is valid, the JSON is right, the middleware order is right and the security policies are really applied. BDD scenarios are few on purpose: they are the agreed examples of the business rules, not another copy of every edge case.

A fourth project, `ApexPost.Testing`, holds the test doubles and factories that more than one level needs (the in-memory shipment store, the fake Week 3 branches, the scripted HTTP server, the temporary Couchbase Lite database, the two `WebApplicationFactory` classes and the SQLite test database). It contains no tests, so each double is written once.

### BDD levels

| Feature | Runs at | Why |
|---|---|---|
| `ShipmentCreation.feature` | API (`WebApplicationFactory`, SQLite test database, test JWT) | The business rules here are what a caller sees: 201 with a Location header, 409 for a duplicate and 400 for a weight outside 0.1–70 kg. The weight limit is enforced by request validation, which exists only at the API boundary, so a service-level scenario could not prove it. |
| `ScanSync.feature` | Service (the Part A outbox on a real Couchbase Lite database, wired in process to the real `ShipmentService`) | The rules are about what the device and the server do with scan ids: uploaded once, duplicates skipped, invalid scans rejected with a reason. HTTP adds nothing to that decision and would make the scenarios slower and noisier. The HTTP path is already covered by the integration tests. |
| `AccessControl.feature` | API | Roles and the courierId check are enforced by the authentication and authorization middleware, so only an API-level scenario proves them. |

## 3. Framework choice: xUnit

One framework is used across the whole solution: **xUnit 2.9.3** with Moq 4.20.72 and Reqnroll's xUnit plugin.

| Reason | Detail |
|---|---|
| Consistency | All four existing test projects (Week 3 and Week 4, over 500 test cases) already use xUnit. Moving to NUnit would mean rewriting every test for no gain in confidence. |
| Test isolation | xUnit creates a new instance of the test class for every test. There is no `[SetUp]` state that leaks from one test into the next, which supports the "no shared mutable state" rule. |
| Setup and teardown | Plain C#: the constructor arranges, `IDisposable` / `IAsyncLifetime` tears down. Expensive objects (a `WebApplicationFactory`, a SQL Server database) are shared on purpose through `IClassFixture`, and only when they hold no state a test depends on. |
| Parameterised tests | `[Theory]` with `[InlineData]` for boundary tables, `[MemberData]` for generated tables (for example every pair of shipment statuses). |
| Reqnroll support | `Reqnroll.xUnit` generates xUnit tests from feature files, so scenarios show up in Test Explorer and `dotnet test` like any other test. |
| Team familiarity | The same attributes, assertions and runner as Weeks 3 and 4. |
| Parallelism | Test classes run in parallel by default. Tests that touch a shared resource (the SQL Server database) are put in one collection. |

Moq is used for interaction-based isolation (`Setup`, `ReturnsAsync`, `It.Is`, `It.IsAny`, `Verify` with `Times`). Hand-written fakes are kept where a test needs real behaviour across several calls (for example the in-memory shipment store that enforces uniqueness). `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` replaces the hand-written `TestClock`, so time is always fixed and explicit.

## 4. Review of the existing tests

Every existing test class was reviewed against the risk map and the rules in section 7. "Kept" means the tests stay as they were apart from the move to the new project and namespace. "Modified" says what changed. Every test method in every kept or modified class was renamed to the `Method_Scenario_ExpectedResult` convention and checked for the AAA layout; that is not repeated in each row. No test was deleted without a reason in this table.

### Week 3: `ApexPost.Test`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ApiKeyAuthenticationMiddlewareTests` | Kept | Unit / CounterPlatform | Middleware run on a `DefaultHttpContext`: fast, isolated, protects partner API keys. |
| `AuthorizationTests` | Kept | Unit / CounterPlatform | Same-branch policy handler tested directly. |
| `CourierRatePerformanceTests` | Modified, renamed `CourierRateConcurrencyTests` | Unit / CounterPlatform | It asserts the concurrency limit and the cache, not timing, so "Performance" was the wrong name. The small delay in the fake only creates overlap; the assertions do not depend on it. |
| `CourierTrackingTests` | Kept | Unit / CounterPlatform | Timeout versus caller cancellation of the simulated courier service. |
| `DomainBehaviourTests` | Kept | Unit / CounterPlatform | Week 3 domain rules (R9). |
| `DomainEdgeCaseTests` | Kept | Unit / CounterPlatform | Boundary cases of the Week 3 domain (R9). |
| `DomainRulesTests` | Kept | Unit / CounterPlatform | The four Week 3 business rules and pricing (R9). |
| `ErrorMappingAndOverPostingTests` | Modified | Integration / CounterPlatform | A static counter gave each test a client address; that is shared mutable state, so each test now derives a fixed address from its own name. |
| `ForwardedHeadersPipelineTests` | Kept | Integration / CounterPlatform | Middleware order can only be proved through the real pipeline. |
| `IdentityVerificationTests` | Kept | Unit / CounterPlatform | Masking and cancellation of the identity check. |
| `IntegrationTests` | Modified, renamed `WebPipelineTests` | Integration / CounterPlatform | "IntegrationTests" said nothing about what it covers. A static counter was replaced as above. |
| `IpRestrictionMiddlewareTests` | Kept | Unit / CounterPlatform | Allow-list middleware tested directly. |
| `MaintenanceModeMiddlewareTests` | Kept | Unit / CounterPlatform | 503 and Retry-After during maintenance. |
| `PersistenceModelTests` | Kept | Integration / CounterPlatform | Checks the EF Core model, migrations and SQL translation with the real provider and no server. It does not mock `DbContext`. |
| `ProductCataloguePerformanceTests` | Modified, renamed `ProductCatalogueCacheTests` | Unit / CounterPlatform | It measured elapsed time with a `Stopwatch` and printed it. The time was never asserted and made the output differ on every run, so the stopwatch was removed; the deterministic assertion (repository load count) stays. |
| `SqlServerIntegrationTests` | Kept | Integration / CounterPlatform | Constraints, row versions, deadlocks, stored procedures and least-privilege roles exist only in SQL Server. Opt-in through `APEXPOST_TEST_SQLSERVER`. |
| `StaffManagementServiceTests` | Kept | Unit / CounterPlatform | Service with an in-memory fake database; it needs real behaviour across calls, so a fake suits it better than Moq. |
| `TransactionWorkflowTests` | Kept | Unit / CounterPlatform | Checkout, refunds and stock (R9). |
| `WebWorkflowIntegrationTests` | Modified | Integration / CounterPlatform | Static counter replaced as above. |
| Support: `ApexPostWebApplicationFactory`, `Fakes/InMemoryDatabase`, `TestOptionsMonitor` | Moved | `ApexPost.Testing` | Used by both unit and integration tests. |

### Week 4 Part B to D: `ApexPost.Api.Tests`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ClientSdkTests` | Kept | Integration / Api | The generated NSwag client and the console consumer against the in-process API. |
| `ContractTests` | Kept | Integration / Api | Compares the served OpenAPI documents with `contracts/`, so a breaking change cannot slip in. |
| `CourierAppEndToEndTests` | Modified | Integration / Api | It drove the MAUI app's view models, which are presentation code and out of scope this week. It now drives the same `ScanSyncService` and Couchbase Lite repository the view models call, so it still proves the device-to-API flow. |
| `ScanEndpointTests` | Kept | Integration / Api | Scan upload and history over HTTP (R3). |
| `SecurityTests` | Modified | Integration / Api | Expired and valid tokens used `DateTimeOffset.UtcNow`; they now use fixed dates. A test that timed the phone regular expression with a `Stopwatch` was replaced by a deterministic check that the pattern cannot backtrack. Signing and partner keys are generated per run instead of being written in the code. |
| `ShipmentEndpointTests` | Kept | Integration / Api | Every verb and status code of Part B. |
| `ShipmentRulesTests` | Modified, renamed `ShipmentStatusTests` | Unit / Tracking | Extended to every from/to pair with `[MemberData]` (R1). |
| `ShipmentServiceTests` | Kept | Unit / Tracking | State-based service tests with the in-memory store. New Moq tests sit beside them for the interactions (what is saved, and what must never be saved). |
| `SqlServerTrackingTests` | Modified | Integration / Tracking | A static tracking-number counter was replaced by numbers derived from each test's name. Opt-in. |
| `VersioningTests` | Kept | Integration / Api | v1 and v2 shapes and headers. |
| Support: `ApiFactory`, `TestDoubles` | Moved | `ApexPost.Testing` | `TestClock` replaced by `FakeTimeProvider`. |

### Week 4 Part A: `ApexPost.Courier.Offline.Tests`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ChangeListenerTests` | Modified | Integration / Offline | A fixed `Task.Delay(750)` waited for notifications that should not come. It now waits on an explicit condition with a bound, so the result does not depend on machine speed. |
| `CouchbaseScanRepositoryTests` | Kept | Integration / Offline | Key-value CRUD against the real embedded database. Couchbase Lite is never mocked. |
| `CourierDatabaseTests` | Kept | Integration / Offline | Database lifecycle, indexes and listener removal. |
| `DocumentModelTests` | Modified | Integration / Offline | Used `DateTimeOffset.UtcNow` for scan times; now a fixed time. |
| `HarnessAppTests` | Modified | Integration / Offline | `Thread.Sleep` and `Task.Delay` replaced by waits on the condition the test needs. |
| `OutboxServiceTests` | Modified, renamed `OutboxCouchbaseTests` | Integration / Offline | These run the outbox on a real database, so they are integration tests. The unit tests for the outbox are new and use Moq. |
| `ScanQueryTests` | Kept | Integration / Offline | SQL++ statements, parameters and index use. |
| `ShipmentScanTests` | Kept | Unit / Offline | Pure domain: scan ids, UTC conversion, validation. |
| Support: `TestSupport` | Moved | `ApexPost.Testing` | |

### Week 4 Part D: `ApexPost.Courier.Core.Tests`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ApiClientTests` | Kept, renamed `ApexApiClientPipelineTests` | Unit / Courier | The typed client, auth handler and JSON options with an in-memory `HttpMessageHandler`. No network. |
| `ResilienceTests` | Kept | Integration / Courier | Runs the real Polly pipeline (retry, circuit breaker, timeouts) with very short settings. It composes several real components, so it is an integration test. |
| `SyncTests` | Kept, renamed `ScanSyncTests` | Integration / Courier | Batch sync with a real Couchbase Lite database and a scripted server. |
| `ViewModelTests` | **Removed** | | MAUI presentation is out of scope for Week 5. What these tests protected is still covered: a 401 clears the token and signs out (`ApexApiClientPipelineTests`), scans saved offline stay Pending and upload on reconnect (`ScanSyncTests`, `CourierAppEndToEndTests`, `ScanSync.feature`). |
| Support: `TestSupport` | Moved | `ApexPost.Testing` | |

### New test classes

| Class | Level | Risk |
|---|---|---|
| `TrackingNumberTests`, `ShipmentWeightTests` | Unit | R7: format and 0.1–70 kg boundaries, exceptions |
| `RequestValidationTests` | Unit | R7: the DTO attributes and the custom rules (future scan time, Delivered needs a signature) |
| `ShipmentServiceMockTests` | Unit | R1, R2, R3 with Moq: what is saved, what is never saved |
| `OutboxServiceTests` | Unit | R6 with Moq (drafted with Copilot, reviewed) |
| `ApexApiClientResponseTests` | Unit | R5: 200 with JSON, 404, 500, timeout and cancellation, ProblemDetails, with a fake `HttpMessageHandler` |
| `ClosedShipmentScanTests` | Unit | R8, built test-first |
| `EfShipmentStoreTests` | Integration (SQLite) | R2, R3: the real queries, paging, latest-scan projection, cascade delete |
| `ShipmentApiDatabaseTests` | Integration (SQLite + `WebApplicationFactory` + test JWT) | R2, R4: an endpoint end to end against a real database |
| `ShipmentCreation.feature`, `ScanSync.feature`, `AccessControl.feature` | BDD | R2, R3, R4, R7, R8 |

## 5. Data access strategy

| Approach | Used for | What it proves | What it does not prove |
|---|---|---|---|
| **Moq mock of a repository interface** (`IShipmentStore`, `IScanRepository`, `IShipmentUploader`, `IBranchRepository`) | Service unit tests | The service's decisions and interactions: a duplicate is never added, nothing is saved after a refusal, a transient error leaves a scan Pending | Anything about storage: that a query is valid, that the result is filtered or ordered, that a constraint or transaction holds |
| **Hand-written in-memory fake** (`InMemoryShipmentStore`, Week 3 `InMemoryDatabase`) | Stateful service workflows and fast API tests | Behaviour across several calls, with simple uniqueness rules | SQL translation, collation, concurrency tokens, cascades |
| **SQLite in-memory** (real `TrackingDbContext` and `EfShipmentStore`) | Repository integration tests, the API end-to-end test, the shipment creation scenarios | Real LINQ-to-SQL translation, relational behaviour, the unique index, foreign keys and cascade delete, paging and ordering, the latest-scan projection | SQL Server behaviour: its case-insensitive collation (the city filter), `rowversion` concurrency, the SQL Server check constraints, the mapping of SQL Server error numbers to `DuplicateValueException`, migrations, and query plans. SQLite also needs a small test-only model adjustment (dates stored as numbers, SQL Server-only check constraints removed), which is itself a difference. |
| **SQL Server test database** (`APEXPOST_TEST_SQLSERVER`) | Release gate and CI with a database service | Migrations, constraints, row versions, deadlocks, stored procedures, roles, the racing duplicate and parallel uploads | Nothing extra about business rules, and it needs a server, so it is opt-in. Each run creates and drops its own disposable database and never touches `ApexPostDb`. |
| **Embedded Couchbase Lite in a temporary folder** | Part A repository, queries, indexes, listener | The real device database, SQL++ and index use | Nothing about the server side |

**Why `DbContext` and `DbSet` are not mocked.** A mocked `DbSet` runs LINQ-to-Objects in memory, not SQL. It would pass a query that the provider cannot translate (and throws for at run time), it compares strings with C# rules instead of the database collation, it ignores `Include`, projections, ordering of equal keys, unique indexes, foreign keys, cascades, concurrency tokens and transactions, and it ties the test to the exact sequence of EF calls instead of the result. A test like that passes while production fails, which is false confidence. Query logic is therefore tested against a real engine (SQLite always, SQL Server when configured), and services are isolated at the repository interface instead.

## 6. What is not tested, and why

| Not tested | Why |
|---|---|
| MAUI pages, view models, navigation and platform services | Out of scope for Week 5. The logic they call (client, sync, outbox) is tested directly. |
| Browser, Razor rendering details, CSS, JavaScript | Out of scope. The MVC tests check status codes, redirects and key content, not layout. |
| Third-party internals (EF Core, Polly, Couchbase Lite, ASP.NET Core) | They have their own tests. We test our configuration of them (retry rules, model, policies). |
| The NSwag-generated client code | Generated, never edited. It is exercised once through `ClientSdkTests`. |
| Performance and load | Not deterministic in a unit test run. The Week 3 load test script stays separate. |
| Real external networks, emails and production databases | Forbidden by the constraints, and unnecessary: every boundary has a double or an in-process server. |
| Trivial code (records, DTO properties, simple mapping with no rule) | No risk to protect; covered indirectly by the API tests. |

## 7. Conventions

- **Naming:** `Method_Scenario_ExpectedResult`, for example `UploadScansAsync_SameScanIdTwice_CountsSecondAsDuplicate`. For a class that tests one rule rather than one method, the first part names the rule.
- **AAA:** every test has `// Arrange`, `// Act` and `// Assert` sections, in that order. A test that checks an exception uses `// Act` with a delegate and asserts in `// Assert`. Where nothing needs arranging the section is left out.
- **Deterministic:** no `DateTime.Now` or `DateTimeOffset.UtcNow` in tests (time comes from `FakeTimeProvider` or fixed dates), no dependence on test order, no static mutable state, no fixed sleeps. Asynchronous notifications are awaited on the condition the test needs, with a bound.
- **Fictional data only:** tracking numbers `APX-1xxxxxxx`, couriers `CR-1001`, `.example` domains.
- **No secrets:** signing keys and partner keys are generated per test run. The SQL Server connection string comes only from an environment variable.
- **Copilot-assisted classes** start with a comment that says so and points to `COPILOT-LOG.md`.

## 8. Running the tests

```bash
dotnet test ApexPost.slnx
```

This needs nothing installed beyond the .NET 10 SDK: SQLite runs in memory, Couchbase Lite in a temporary folder and the API in process. The SQL Server tests report as skipped unless `APEXPOST_TEST_SQLSERVER` is set. The README has the filters for each level and the coverage command.

## 9. Results and reflection

Completed after the full run (Task 7).
