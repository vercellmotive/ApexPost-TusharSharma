# ApexPost testing strategy

This is the plan for proving that ApexPost is reliable and safe to change before the next release. It was written before any Week 5 test was added or changed: it is the first commit on `week5-testing`, `df1d417`. After the work was done, the tables were updated to name the classes as built, and section 9 (results, reflection and viva notes) was added. The Git history shows both versions.

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

**Actual, after the work** (test cases in the default run, and with SQL Server): unit **316** (46 %), integration **315 + 36 SQL Server** (51 %), acceptance **24** (3 %). The integration share is higher than planned because the inherited Week 3 web and Week 4 API suites are mostly `WebApplicationFactory` tests, and they were kept on purpose: they guard the pipeline, the security policies and the contracts, which unit tests cannot. Almost every new test was a unit test. Section 9 says what would change.

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
| Reqnroll support | `Reqnroll.xUnit` generates xUnit tests from feature files, so scenarios show up in Test Explorer and `dotnet test` like any other test. Weeks 1 to 4 had no SpecFlow work, so there was nothing to migrate: Reqnroll, the open-source successor to SpecFlow, was used from the start. |
| Team familiarity | The same attributes, assertions and runner as Weeks 3 and 4. |
| Parallelism | Test classes run in parallel by default. Tests that touch a shared resource (the SQL Server database) are put in one collection. |

Moq is used for interaction-based isolation (`Setup`, `ReturnsAsync`, `It.Is`, `It.IsAny`, `Verify` with `Times`). Hand-written fakes are kept where a test needs real behaviour across several calls (for example the in-memory shipment store that enforces uniqueness). `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` replaces the hand-written `TestClock`, so time is always fixed and explicit.

## 4. Review of the existing tests

Every existing test class was reviewed against the risk map and the rules in section 7. "Kept" means the tests stay as they were apart from the move to the new project and namespace. "Modified" says what changed. Every test method in every kept or modified class was renamed to the `Method_Scenario_ExpectedResult` convention and checked for the AAA layout; that is not repeated in each row. The renaming took two passes. The second pass, after the merge, caught 181 names that still had only two parts, and [`docs/week5/test-renames.md`](docs/week5/test-renames.md) lists each old and new name.

A review of the AAA layout after the merge found 157 tests that did not keep it strictly:
- some combined Act and Assert in one section;
- some acted twice, with a second `// Arrange (next step)`;
- some called a response-checking helper before `// Assert`.

Each of them now has one Act and one Assert. No test was split or dropped, and the counts did not change. The same review renamed three tests whose name parts were in the wrong order. No test was deleted without a reason in this table.

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
| Support: `ApexPostWebApplicationFactory`, `Fakes/InMemoryDatabase`, `TestOptionsMonitor` | Moved | `ApexPost.Testing` | Used by both unit and integration tests. The factory now fixes the counter's clock with a `FakeTimeProvider`, and the integration key is generated per run. The demo staff sign-in pattern moved from the tests into `testsettings.json`. |

### Week 4 Part B to D: `ApexPost.Api.Tests`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ClientSdkTests` | Kept | Integration / Api | The generated NSwag client and the console consumer against the in-process API. |
| `ContractTests` | Kept | Integration / Api | Compares the served OpenAPI documents with `contracts/`, so a breaking change cannot slip in. |
| `CourierAppEndToEndTests` | Modified | Integration / Api | It drove the MAUI app's view models, which are presentation code and out of scope this week. It now drives the same `ScanSyncService` and Couchbase Lite repository the view models call, so it still proves the device-to-API flow. |
| `ScanEndpointTests` | Kept | Integration / Api | Scan upload and history over HTTP (R3). |
| `SecurityTests` | Modified | Integration / Api | Expired and valid tokens used `DateTimeOffset.UtcNow`; they now use fixed dates. A test that timed the phone regular expression with a `Stopwatch` was replaced by a deterministic check that the pattern cannot backtrack. Signing and partner keys are generated per run instead of being written in the code. |
| `ShipmentEndpointTests` | Kept | Integration / Api | Every verb and status code of Part B. |
| `ShipmentRulesTests` | Modified, renamed `ShipmentStatusTests` | Unit / Tracking | Extended to every one of the 31 disallowed from/to pairs with `[MemberData]`, plus the Delivered to InTransit case from the brief (R1). Its tracking number and weight tests moved to the new `TrackingNumberTests` and `ShipmentWeightTests`. |
| `ShipmentServiceTests` | Kept | Unit / Tracking | State-based service tests with the in-memory store. New Moq tests sit beside them for the interactions (what is saved, and what must never be saved). |
| `SqlServerTrackingTests` | Modified | Integration / Tracking | A static tracking-number counter was replaced by numbers derived from each test's name. Opt-in. |
| `VersioningTests` | Kept | Integration / Api | v1 and v2 shapes and headers. |
| Support: `ApiFactory`, `TestDoubles` | Moved | `ApexPost.Testing` | `TestClock` replaced by `FakeTimeProvider`. |

### Week 4 Part A: `ApexPost.Courier.Offline.Tests`

| Class | Decision | New home | Reason |
|---|---|---|---|
| `ChangeListenerTests` | Modified | Integration / Offline | A fixed `Task.Delay(750)` waited for notifications that should not come. A second listener added for the test now shows when Couchbase Lite has delivered the changes, so the test waits exactly as long as needed. |
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
| `ResilienceTests` | Modified | Integration / Courier | Runs the real Polly pipeline (retry, circuit breaker, timeouts) with very short settings. It composes several real components, so it is an integration test. A fixed wait for the end of the circuit break was replaced by calling until the trial call gets through, with a bound. |
| `SyncTests` | Kept, renamed `ScanSyncTests` | Integration / Courier | Batch sync with a real Couchbase Lite database and a scripted server. |
| `ViewModelTests` | **Removed** | | MAUI presentation is out of scope for Week 5. What these tests protected is still covered: a 401 clears the token and signs out (`ApexApiClientPipelineTests`), scans saved offline stay Pending and upload on reconnect (`ScanSyncTests`, `CourierAppEndToEndTests`, `ScanSync.feature`). |
| Support: `TestSupport` | Moved | `ApexPost.Testing` | |

### New test classes

| Class | Project | Level | Risk |
|---|---|---|---|
| `TrackingNumberTests`, `ShipmentWeightTests` | UnitTests / Tracking | Unit | R7: format and 0.1 to 70 kg boundaries, `ParamName` of each exception |
| `RequestValidationTests` | UnitTests / Api | Unit | R7: every DTO rule through the DataAnnotations validator, the custom `[NotInFuture]` with a fake clock, the Delivered signature rule, batch size 0, 1, 100 and 101 |
| `ShipmentServiceMockTests` | UnitTests / Tracking | Unit | R1, R2, R3 with Moq: what is saved once, and what must never be added, saved or removed |
| `ClosedShipmentScanTests`, `ShipmentClosingTests` | UnitTests / Tracking | Unit | R8, built test-first (TDD-LOG.md) |
| `OutboxServiceTests` | UnitTests / Offline | Unit | R6 with Moq (drafted with Copilot, reviewed: COPILOT-LOG.md) |
| `ApexApiClientResponseTests` | UnitTests / Courier | Unit | R5: 200 with JSON, 404 with and without ProblemDetails, 5xx, a 400 with validation errors, timeout, cancellation, network failure and an unreadable body, with a fake `HttpMessageHandler` and one Moq-mocked handler |
| `EfShipmentStoreTests` | IntegrationTests / Tracking | Integration (SQLite) | R2, R3: filtering, paging, ordering, the latest-scan projection, scan ownership, idempotent upload, cascade delete, the unique index |
| `ShipmentApiDatabaseTests` | IntegrationTests / Api | Integration (SQLite, `WebApplicationFactory`, test JWT) | R2, R3, R4: requests in, rows out |
| `ShipmentCreation.feature` | Specs | BDD, API level | R2, R7 |
| `ScanSync.feature` | Specs | BDD, service level | R3, R6, R8 |
| `AccessControl.feature` | Specs | BDD, API level | R4 (drafted with Copilot, reviewed) |

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

- **Naming:** every test name is `Method_Scenario_ExpectedResult`, for example `UploadScansAsync_SameScanIdTwice_CountsSecondAsDuplicate`.
  - The first part is what the test exercises: a method, an HTTP action for the web tests (`PostCounterCreate`, `GetParcelStatus`), or the subject of a test that checks a whole model or layer (`Model`, `SourceFiles`).
  - The second part is the situation, and the third is the expected result.
  - Every one of the 464 test methods has all three parts. No name has only two, and none splits the result with an extra `_And…` part.
- **AAA:** every test has one `// Act` and one `// Assert` section, in that order, after an `// Arrange` section when there is something to set up. Nothing before `// Assert` asserts.
  - **Exceptions.** When only the exception matters, `// Act` holds the call as a delegate (`Action act = () => …;`) and `// Assert` runs `Assert.Throws<T>(act)`. When the state after the failure matters too, for example that no stock moved, `// Act` makes the call through `Record.Exception` or `Record.ExceptionAsync`, and `// Assert` checks the exception type and then the state.
  - **Several calls.** When a test compares related calls, such as each role against the same endpoint, `// Act` makes all of them and keeps each result in a named variable, and `// Assert` checks them all.
  - **Journeys.** When a later step depends on an earlier one, such as a sale before a void, the earlier steps run in `// Arrange`. Any result the test still checks is kept in a variable and asserted in `// Assert`.
  - **Helpers.** Helpers that only set a test up (`SignInAsync`, `TokenFrom`, `CreateShipmentAsync`, the harness's `RunAsync`) throw on a broken setup and never assert. Helpers that check a response (`ReadProblem`, `ReadValidationErrors`, `AssertProblemAsync`) are called only in `// Assert`.
  - **Checked automatically.** `python3 tools/check-test-conventions.py` checks the naming and AAA rules for every test and lists any that break them. It reports 464 tests checked and 0 breaking the rules. On the code as first merged it reported 179, which a review after the merge fixed (section 4 and `docs/week5/test-renames.md`).
- **Deterministic:** no `DateTime.Now` or `DateTimeOffset.UtcNow` in any test project (time comes from `FakeTimeProvider` or fixed dates; tokens expire in 2099 or expired in 2021), no dependence on test order, and no static mutable state (per-test values are derived from the test's name). There are no fixed sleeps: asynchronous work is awaited on the condition the test needs, with a bound that only stops a broken test from hanging. The only real delays left are inside test doubles that play a slow server or a slow courier service, so that Polly's real timeouts and the concurrency limit have something to act on; their margins are ten times or more.
- **Fictional data only:** tracking numbers `APX-1xxxxxxx`, couriers `CR-1001`, `.example` domains.
- **No secrets:** signing keys and partner keys are generated per test run. The SQL Server connection string comes only from an environment variable.
- **Copilot-assisted classes** start with a comment that says so and points to `COPILOT-LOG.md`.

## 8. Running the tests

```bash
dotnet test ApexPost.slnx
```

This needs nothing installed beyond the .NET 10 SDK: SQLite runs in memory, Couchbase Lite in a temporary folder and the API in process. The same tests appear in Visual Studio Test Explorer, grouped by project, with the Reqnroll scenarios listed by name. The SQL Server tests report as skipped unless `APEXPOST_TEST_SQLSERVER` is set. The README has the commands for each level and for coverage.

## 9. Results and reflection

### Results

| Run | Unit | Acceptance | Integration | Total |
|---|---|---|---|---|
| `dotnet test ApexPost.slnx`, no setup | 316 passed | 24 passed | 315 passed, 36 skipped | 655 passed, 0 failed, 36 skipped |
| With SQL Server 2022 (`APEXPOST_TEST_SQLSERVER`) | 316 passed | 24 passed | 351 passed | 691 passed, 0 failed, 0 skipped |

Logs: `docs/week5/test-run.txt`, `docs/week5/test-run-with-sql-server.txt`; screenshot: `docs/week5/test-run.png`.

**Coverage** (default run, coverlet and ReportGenerator, `docs/week5/coverage/Summary.txt`): 78.1 % of lines and 67.6 % of branches. Coverage is evidence of where tests reach, not a target. It shows what the strategy predicted:

- `ApexPost.Tracking` 97.7 % and `ApexPost.Courier.Offline` 95.9 %: the code with the highest risks is the best covered.
- `ApexPost.Courier.Core` 64.7 %: the gap is the MAUI view models (0 %), out of scope by decision (section 6). The client, handlers, resilience and sync code around them are covered.
- `ApexPost.Tracking.Infrastructure` 73.5 % and `ApexPost.Infrastructure` 69.2 %: the uncovered lines are SQL Server-only paths, such as error 2601/2627 mapping, the racing-insert fallback and stored procedures. The SQL Server run covers them; the default run cannot, which is exactly what section 5 says SQLite does not prove.
- A high number is not proof. `TrackingNumber.IsValid` was already fully covered by the Week 4 tests while the trailing line-break defect sat in it. Only a boundary value found it.

### What the strategy caught

1. **A real defect, found by boundary values (R7).** `TrackingNumber.IsValid("APX-12345678\n")` and the phone pattern returned true, because `$` in .NET also matches before a final line break. A request with that tracking number passed validation and would then have failed the SQL Server check constraint with a 500. Two `[InlineData]` rows found it; the fix is commit `3f34acd`.
2. **A time-of-day dependency.** The determinism rule (no real clock in tests) showed that the Week 3 counter tests depended on the real time: a sale is refused outside opening hours and dated from the clock. `TransactionService` now takes a `TimeProvider`, and the tests fix the time.
3. **A missing business rule (R8)**, now built test-first: a late scan could make a delivered parcel appear to move again.
4. **Weak Copilot tests.** An unset mock that made the outbox treat every scan as deleted, a duplicated step binding that broke a whole feature, and a conditional assertion that checked nothing on the success path. All three would have looked fine in a quick review.

### What I would change

- **Move rule checks down the pyramid.** Many inherited API tests check rules such as status moves or the scan rejection reasons through HTTP. Most of those rules now have unit tests, so the API tests could keep one example per rule and the status code, and leave the variations to the unit tests. That would bring the pyramid closer to the 65/33 plan and shorten the integration run.
- **Run the SQL Server tests in CI on every pull request,** with a SQL Server service container. Today they are opt-in, so a developer can skip the only tests that prove the migrations, row versions and SQL Server error mapping.
- **Use a fake clock for Polly as well.** The resilience tests still use real (short) delays. Polly v8 reads its time from `TimeProvider`, so the circuit breaker could be tested with `FakeTimeProvider.Advance` instead of waiting.
- **Mutation testing.** The three deliberate breaks in COPILOT-LOG.md were done by hand. A tool such as Stryker.NET (free) would do the same across the whole domain and show which tests can never fail.

### Most confidence per minute

**The unit tests with `[Theory]` boundary tables.** They took minutes to write, run in under a second, and found the only production defect of the week. Next were the SQLite repository tests: a few lines of setup gave real SQL for every query, with none of the false confidence of a mocked `DbSet`. The BDD scenarios gave the least confidence per minute but the most shared understanding. The access-control feature reads as the rules a business owner would sign off, and two of its scenarios caught the deliberate security breaks.

### Viva notes (five minutes)

1. **The pyramid (1 minute).** 316 unit, 351 integration (36 of them SQL Server), 24 acceptance. Unit tests carry the rules: status moves, weights, tracking numbers, duplicates, the outbox, the typed client. Integration tests prove what unit tests cannot: real SQL on SQLite and SQL Server, Couchbase Lite, the HTTP pipeline, JWT policies and the contracts. A few Gherkin scenarios state the business rules. The integration share is higher than planned because of the inherited web tests; section 9 says what I would change.
2. **The framework (1 minute).** xUnit: all 500+ existing tests already used it. It gives isolation by default (a new instance per test, no shared `[SetUp]` state), constructor and `IDisposable` for setup and teardown, `[Theory]` with `[InlineData]` and `[MemberData]`, and Reqnroll's xUnit plugin, so scenarios run in Test Explorer and `dotnet test` like any other test.
3. **One mock and what it does not prove (1 minute).** In `ShipmentServiceMockTests`, `IShipmentStore` is a strict Moq mock. `CreateAsync_ExistingTrackingNumber_ReturnsConflictAndNeverAddsOrSaves` sets `ExistsAsync` to return true and verifies that `Add` and `SaveChangesAsync` are called `Times.Never`. It proves the service's decision. It does not prove that `ExistsAsync` really finds a duplicate, or that two requests racing past the check are stopped: that needs the unique index, which `EfShipmentStoreTests` (SQLite) and `SqlServerTrackingTests` (the racing insert on SQL Server) prove.
4. **One Copilot test I rejected (1 minute).** Copilot's step "any access denial is returned as Problem Details" checked the body only when the status happened to be 401 or 403. For the Admin delete (204) and the partner read (200) it passed while asserting nothing, which looks like coverage but is not. I replaced it with separate scenarios that always assert: the row is really deleted, and the partner reply really hides the recipient. A deliberate break of the Admin-only rule (mutation 3) then failed exactly one of those scenarios.
5. **What it caught (1 minute).** The trailing line-break defect, the time-of-day dependency, and the late-scan rule built with RED, GREEN and REFACTOR commits.
