# ApexPost Logistics Counter Platform

ApexPost is a .NET 10 / C# 14 ASP.NET Core MVC counter platform for branch retail sales, shipping, money orders, identity checks, inventory, reporting, and audit. The solution uses a layered domain/application/infrastructure/web architecture. Data is stored in Microsoft SQL Server through EF Core 10, behind repository and query interfaces.

**Repository:** <https://github.com/vercellmotive/ApexPost-TusharSharma> (it was `vercellmotive/ApexPost`, and the old address redirects). A zip has no Git history, so [`docs/GIT-HISTORY.md`](docs/GIT-HISTORY.md) lists the commits and pull requests.

## Requirements and run

**Run order for the Week 4 API:** (1) create the Week 3 database with steps 1 to 3 below, then run `ApexPost.Web` once (step 5) so it seeds the branches, because creating a shipment checks its origin branch against them (the migrations alone add no branches), (2) set the API's secrets (the JWT signing key and a partner key, shown under Week 4 Part D), (3) start the API with `dotnet run --project ApexPost.Api --launch-profile https`, then open `https://localhost:7180/swagger`. The courier app comes after that (see Part D).

You need the .NET 10 SDK and a SQL Server you control. Free options include SQL Server Developer or Express, LocalDB on Windows, or the Developer image in Docker, plus SSMS. Docker Desktop itself needs a paid plan at larger companies. Never use a shared training database.

1. Restore the local `dotnet-ef` tool and the solution's packages. `dotnet ef` fails with `NETSDK1004` if the projects haven't been restored:

```bash
dotnet tool restore
```

```bash
dotnet restore ApexPost.slnx
```

2. Set the connection string for your server. It stays out of source control. On LocalDB, for example:

```bash
dotnet user-secrets set "ConnectionStrings:ApexPostDb" "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True" --project ApexPost.Web
```

3. Create `ApexPostDb` and apply the migrations. Set `APEXPOST_DESIGN_CONNECTION` to the same connection string first, because design-time tools don't read user-secrets:

```bash
dotnet ef database update --project ApexPost.Infrastructure --startup-project ApexPost.Infrastructure
```

If you prefer SSMS, run `sql/schema/ApexPostDb-idempotent.sql` in an empty `ApexPostDb` instead.

4. Set a partner API key (needed only for `/integration/parcel-status`):

```bash
dotnet user-secrets set "Integration:ApiKey" "Choose-a-long-random-partner-key" --project ApexPost.Web
```

5. Run the app. In Development the first start seeds 5 branches, 20 staff, 30 products, 10 zones with 60 rates, and over 200 historical transactions. All of it is fictional.

```bash
dotnet run --project ApexPost.Web --launch-profile https
```

Open the HTTPS URL it prints and sign in with an account below. Warnings fail the build through `Directory.Build.props`.

If it stops with `Unable to configure HTTPS endpoint`, your machine has no development certificate. Create and trust one, which may ask for your password, then run the app again:

```bash
dotnet dev-certs https --trust
```

To regenerate the idempotent script after adding a migration:

```bash
dotnet ef migrations script --idempotent --project ApexPost.Infrastructure --startup-project ApexPost.Infrastructure -o sql/schema/ApexPostDb-idempotent.sql
```

### Tests

```bash
dotnet test ApexPost.slnx
```

This runs the Week 3 project `ApexPost.Test` (181 tests with no database, and 28 more reported as skipped), and the Week 4 projects `ApexPost.Courier.Offline.Tests` (97 tests that use an embedded database and need no server), `ApexPost.Api.Tests` (144 tests with no database, and 8 more reported as skipped) and `ApexPost.Courier.Core.Tests` (65 tests). To run the skipped SQL Server tests too, point this at a server you control. The tests create and drop their own disposable database and never touch `ApexPostDb`:

```bash
APEXPOST_TEST_SQLSERVER="Server=localhost;User Id=<user>;Password=<password>;TrustServerCertificate=True" dotnet test ApexPost.slnx
```

### Web configuration reference

Most settings read by `ApexPost.Web` are declared in `ApexPost.Web/appsettings.json`. Secret-valued keys are present as empty strings only to show the configuration shape; they are intentionally unusable until supplied through user-secrets or deployment secrets. Do not replace these placeholders with real credentials in the checked-in file.

| Key | Checked-in default | Purpose |
|---|---|---|
| `ConnectionStrings:ApexPostDb` | Not in source | SQL Server connection, set with user-secrets. |
| `Database:SeedOnStartup` | `false` (`true` in Development) | Seeds an empty database on startup. It never creates or migrates the schema. |
| `Inventory:LowStockThreshold` | `5` | Quantity at or below which a product counts as low stock. |
| `Integration:ApiKey` | Empty | Single-partner API key, reported as partner `default`. |
| `Integration:Partners:<PartnerId>:ApiKey` | `CourierA` shown with empty key | Per-partner API key; add one entry per integration partner. |
| `Security:AllowedIps` | Loopback addresses | Client IP allowlist for integration and administrator routes. |
| `ForwardedHeaders:KnownProxies` | Empty | Explicit trusted reverse-proxy addresses; configure when deployed behind a proxy. |
| `MaintenanceMode:Enabled` | `false` | Enables the maintenance response while leaving `/health` available. |
| `MaintenanceMode:RetryAfterSeconds` | `120` | `Retry-After` value during maintenance. |
| `RateLimiting:Login` | 3 per 60 s | `PermitLimit` and `WindowSeconds` per client IP. |
| `RateLimiting:Partner` | 60 per 60 s | `PermitLimit` and `WindowSeconds` per partner. |
| `CourierRates:MaxConcurrentRequests` | `4` | Concurrent calls allowed to the simulated courier rate service. |
| `CourierRates:SimulatedDelayMs` | `20` | Simulated courier rate latency. |
| `Transactions:MoneyOrderFee` | `10` | Fee per money order in AUD. |
| `Cors:PartnerTrackingWidgetOrigin` | Example HTTPS origin | Browser origin permitted for the partner tracking widget. |
| `Transactions:HighValueMoneyOrderThreshold` | `1000` | Identity-verification threshold in AUD. |
| `Transactions:MaximumMoneyOrderAmount` | `20000` | Maximum money-order principal in AUD. |
| `ShippingPricing:ParcelWeightSurchargePerKilogram` | `1.5` | Default parcel weight surcharge in AUD/kg. |
| `CourierTracking:TimeoutMs` | `1500` | Simulated tracking lookup timeout. |
| `CourierTracking:SimulatedDelayMs` | `50` | Simulated tracking lookup delay. |
| `Performance:SlowActionThresholdMs` | `500` | Action filter warning threshold. |
| `Performance:SlowRequestThresholdMs` | `500` | Request timing warning threshold. |

Settings are validated at startup, so an invalid value stops the app with a clear error.

## Demo staff accounts

Every staff member has their own password, stored only as a salted PBKDF2-SHA256 hash in `staff.json`. These are fictional seed accounts for local demos and must not be used in production.

| Role | Email | Password | Staff ID | Branch |
|---|---|---|---|---|
| Branch Manager | `alice@apexpost.com` | `Apex-Alice-2026!` | STF001 | B001 |
| Counter Clerk | `ben@apexpost.com` | `Apex-Ben-2026!` | STF002 | B001 |
| Auditor | `frank@apexpost.com` | `Apex-Frank-2026!` | STF006 | B002 |
| Administrator | `olive@apexpost.com` | `Apex-Olive-2026!` | STF015 | B001 |

All 20 seeded accounts follow the same pattern, `Apex-<Name>-2026!`, using the capitalised name from the email address. Administrators can set a new password on the staff edit screen. Login allows 3 attempts per minute per IP address, so wait a minute after that.

Branches enforce their real opening hours in their own time zone, so a sale outside those hours is rejected. An administrator can change the hours on the Branches screen to demonstrate sales at any time.

## Features

- Administrator screens to add and edit branches (hours, time zone, terminals), close or reopen them, and add and edit products. Stock rows and the catalogue cache stay in step automatically.
- A transactions screen where Branch Managers void or refund a paid sale, with a required reason.
- Branch terminals, branch operating hours, staff roles, and administrator staff add/edit/deactivate with per-staff passwords. Staff uniqueness/branch rules and ID allocation live in an application service with process-local write serialization.
- A branch-scoped product catalogue and inventory view; `rowversion` and a check constraint stop stock going negative.
- A counter basket mixing retail lines, several shipments and several money orders with an identity check and payment, plus dedicated money order and identity verification screens. Parcel validation, shipping pricing and the identity threshold rule all apply. A sale commits its stock, transaction, lines and payment together. Sales that collide are retried on fresh data, and stock can never go negative; a conflict that outlasts the retries returns a 409.
- Identity verification uses a replaceable simulated format-check service; the clerk cannot mark a check successful. The interface retains only a masked document value and result, and the UI explains that no external identity provider is contacted.
- Branch-manager stock adjustments run through `usp_AdjustStock`, which writes `StockAdjustments` history. They require a quantity change and enumerated reason, enforce branch ownership and non-negative stock, and are included in the append-only audit log.
- Voids and refunds restock retail items in the same commit as the status change.
- Shipping label preview with the recipient address, ten data-backed destination zones and service levels. Parcels need dimensions and have service-level weight limits; letters are limited to 0.5 kg.
- Set-based price revision by category, using `ExecuteUpdateAsync`.
- Reports through query services and stored procedures, with a date picker for daily retail/services sales (opening on the latest day with sales), low stock, shipping volumes, and an auditor-only money-order/identity-verification summary.
- Domain events for low stock, completed transactions, money orders, and void/refund notifications, shown on that branch's dashboard.
- Audit storage in the `AuditEntries` table, committed separately from the sale, covering business actions only (sales, money orders, identity checks, voids, refunds, stock, and staff, branch and product changes), including denied attempts, and `AUDIT-SAMPLE.json`. A failed audit write never fails the business action. Full document numbers, passwords, API keys, and card data are not written to audit entries.
- Configurable shipping surcharge (`ShippingPricing:ParcelWeightSurchargePerKilogram`) and injectable shipping-pricing delegate; transaction status and payment state are mapped to their own columns and tables.
- Cookie authentication with staff, role, and branch claims; antiforgery on state-changing form posts.
- More than 13 active middleware stages for security, request handling, routing, compression, rate limiting, and observability. The exact order is documented in `DESIGN.md`.

## Screenshots and sample requests

Screens captured from the running app are in [`docs/screenshots`](docs/screenshots):

| Screen | File |
|---|---|
| Login | [01-login.png](docs/screenshots/01-login.png) |
| Branch dashboard | [02-branch-dashboard.png](docs/screenshots/02-branch-dashboard.png) |
| Catalogue and stock | [03-catalogue-and-stock.png](docs/screenshots/03-catalogue-and-stock.png) |
| Counter transaction | [04-counter-transaction.png](docs/screenshots/04-counter-transaction.png) |
| Money order | [05-money-order.png](docs/screenshots/05-money-order.png) |
| Identity verification | [06-identity-verification.png](docs/screenshots/06-identity-verification.png) |
| Shipping label | [07-shipping-label.png](docs/screenshots/07-shipping-label.png) |
| Transactions, void and refund | [08-transactions-void-refund.png](docs/screenshots/08-transactions-void-refund.png) |
| Reports | [09-reports.png](docs/screenshots/09-reports.png) |
| Auditor report | [10-auditor-report.png](docs/screenshots/10-auditor-report.png) |
| Staff, branch and product management | [11](docs/screenshots/11-staff-management.png), [12](docs/screenshots/12-branch-management.png), [13](docs/screenshots/13-product-management.png) |

Import [`docs/ApexPost.postman_collection.json`](docs/ApexPost.postman_collection.json) into Postman for sample requests.

## API key and network configuration

Partner API keys are read from user-secrets or deployment configuration. Configure a separate key per partner and do not place real keys in `appsettings*.json` or source control. `Integration:ApiKey` is the simplest option for a single partner:

```bash
dotnet user-secrets set "Integration:ApiKey" "Choose-a-long-random-partner-key" --project ApexPost.Web
```

For multiple partners, use a unique secret for each configured partner ID:

```bash
dotnet user-secrets set "Integration:Partners:CourierA:ApiKey" "Courier-A-long-random-key" --project ApexPost.Web
```

```bash
dotnet user-secrets set "Integration:Partners:CourierB:ApiKey" "Courier-B-long-random-key" --project ApexPost.Web
```

Example request:

```bash
curl -k -H "X-Api-Key: <configured-key>" https://localhost:7125/integration/parcel-status
```

```bash
curl -k https://localhost:7125/health
```

The integration endpoint and administrator staff routes also apply an IP allowlist; the integration endpoint applies a per-partner rate limit. For production behind a reverse proxy, configure `ForwardedHeaders:KnownProxies` with the proxy IP addresses, then configure `Security:AllowedIps` for the actual approved clients. Forwarded headers are accepted only from trusted proxies; trusting arbitrary `X-Forwarded-For` values lets a caller spoof its address. Set `Cors:PartnerTrackingWidgetOrigin` to the approved widget origin. CORS is a browser policy, not authentication or an API security boundary.

The simulated courier tracking lookup has a bounded timeout. Configure `CourierTracking:TimeoutMs` and `CourierTracking:SimulatedDelayMs` to exercise success and timeout behavior; a lookup timeout returns 504, while caller cancellation remains cancellation.

Shipping rates use configured zone multipliers and a tiered service-level base rate. The parcel weight surcharge defaults to AUD 1.50/kg and can be overridden with `ShippingPricing:ParcelWeightSurchargePerKilogram`; negative values fail startup validation.

## Audit sample provenance

`AUDIT-SAMPLE.json` holds real entries from `AuditEntries`, captured while the app ran on SQL Server. To refresh it from SQL Server:

```bash
sqlcmd -d ApexPostDb -Q "SELECT TOP (50) * FROM dbo.AuditEntries ORDER BY TimestampUtc DESC FOR JSON PATH"
```

## Git workflow

Use a short-lived feature branch for each coherent assignment change, push it, and open a pull request with a scope-specific title. Require a warning-free build and passing tests before merge, and never include user-secrets or runtime audit logs. [`docs/GIT-HISTORY.md`](docs/GIT-HISTORY.md) lists the pull requests and their status.

Example local sequence:

```powershell
git switch -c feature/audit-sample-provenance
git add README.md DESIGN.md
git commit -m "docs: make audit evidence reproducible"
git push -u origin feature/audit-sample-provenance
```

## Repeating the blocking demo in SSMS

The evidence in `PERFORMANCE.md` was captured with `sqlcmd`. To repeat it in SSMS on a demo database:

1. Open `sql/transactions/blocking_session_1_writer.sql` and run only its `BEGIN TRANSACTION` and `UPDATE` statements.
2. In a second window, run `sql/transactions/blocking_session_2_reader.sql`. The final query keeps waiting.
3. In a third window, run the `sys.dm_exec_requests` query from step 3 of the first script, and take a screenshot.
4. Run `ROLLBACK TRANSACTION` in the first window. The second window then finishes.

The `sqlcmd` output of the same demo is already in the blocking section of `PERFORMANCE.md`. In `sqlcmd`, these scripts need `-I`, because the filtered index on `BranchStock` requires `QUOTED_IDENTIFIER ON`. SSMS turns it on by default.

## Least-privilege login

Run the app under a restricted login rather than `sa`. Create the login and its password outside source control, apply migrations with a deployment login, run `sql/security/least_privilege_role.sql`, then add the user to the `apexpost_app` role. The app has been tested this way.

## Database artifacts

| Path | Contents |
|---|---|
| `ApexPost.Infrastructure/Persistence/Migrations` | `InitialCreate`, `AddWorkloadIndexes`, `AddStoredProcedures`, `AddRemainingAuditColumns`, `ReplaceLowStockIndexWithFiltered`, `AddShipmentDestinationAddress`, `StampCreatedByInAdjustStock`, `AddShipmentDimensionsCheck`, `ValidateChangedByInAdjustStock`, `TightenPaymentAndRoleColumns`, `DropRedundantShippingRateIndex` |
| `sql/schema` | Generated idempotent schema script, and `schema_fingerprint.sql` for comparing two databases |
| `sql/procedures` | Stored procedures, with `CREATE OR ALTER` |
| `sql/reports` | Reporting queries |
| `sql/transactions` | Atomic sale in T-SQL, and the two-session blocking demo |
| `sql/performance` | Volume generator and evidence scripts for `PERFORMANCE.md` |
| `sql/security` | Least-privilege application role, and a reporting role with no table access |

## Seed data

`DatabaseSeeder` adds the operational rows once, and migrations add reference data. Its source files in `ApexPost.Infrastructure/Data` hold five branches, twenty staff, thirty products and 150 stock rows, and it generates over 200 transactions deterministically, including voids and refunds.

## Week 4 Part A: the courier's offline store

Couriers lose network coverage but must still record pickups and deliveries, so `ApexPost.Courier.Offline` keeps shipment scans on the device in an embedded [Couchbase Lite](https://www.couchbase.com/products/lite/) database named `apexpost_courier`. It uses the free Community Edition NuGet package `Couchbase.Lite` 4.1.2. Couchbase Server and Sync Gateway are not needed, and the Week 3 SQL Server code is untouched. The design reasons are in [`OFFLINE-DESIGN.md`](OFFLINE-DESIGN.md).

| Part | Where |
|---|---|
| Scan model, `IScanRepository`, `IShipmentUploader` (no Couchbase types) | `ApexPost.Courier.Offline/Domain` |
| `CourierDatabase` (opens the database, creates the indexes, listens for changes), `CouchbaseScanRepository`, the SQL++ text | `ApexPost.Courier.Offline/Storage` |
| `OutboxService` and the fake uploader | `ApexPost.Courier.Offline/Sync` |

Run the harness. The database goes in your local application data folder (`~/Library/Application Support/ApexPost/courier` on macOS, `~/.local/share/ApexPost/courier` on Linux, `%LOCALAPPDATA%\ApexPost\courier` on Windows) unless you pass `--data <folder>`:

```bash
dotnet run --project ApexPost.Courier.Offline.Harness
dotnet run --project ApexPost.Courier.Offline.Harness -- --explain
```

The menu is Add Scan, Find by Tracking Number, List Pending, Simulate Sync, Delete Scan and Exit. A new scan is always saved on the device as Pending. Simulate Sync runs the outbox against a fake server, which rejects a scan whose notes contain `[fail]`, so you can see the Failed outcome. The change listener prints the ids of changed documents and a live pending count. Use one program at a time on a data folder (see the design note).

The tests need no network and no server. They open a real embedded database in a temporary folder:

```bash
dotnet test ApexPost.Courier.Offline.Tests
```

Screenshots of the query results and the listener log, and the query plans, are in [`docs/week4-partA`](docs/week4-partA).

## Week 4 Part B: the shipment API

`ApexPost.Api` is an ASP.NET Core Web API with controllers, minimal hosting in `Program.cs`, and no `Startup.cs`. It reuses the Week 3 EF Core registration, and keeps shipments and scans in a new `tracking` schema of the same `ApexPostDb` database. The endpoints, status codes, validation rules and design are in [`API-NOTES.md`](API-NOTES.md).

1. Set the connection string for the API. It reads the same `ApexPostDb` database as the Week 3 app, so use the same value:

```bash
dotnet user-secrets set "ConnectionStrings:ApexPostDb" "Server=(localdb)\MSSQLLocalDB;Database=ApexPostDb;Trusted_Connection=True;TrustServerCertificate=True" --project ApexPost.Api
```

2. Run it. In Development it applies the tracking migrations and adds twelve fictional shipments, `APX-10000001` to `APX-10000012`:

```bash
dotnet run --project ApexPost.Api --launch-profile https
```

To apply the tracking migrations yourself instead, set `APEXPOST_DESIGN_CONNECTION` as for Week 3 and run:

```bash
dotnet ef database update --project ApexPost.Tracking.Infrastructure --startup-project ApexPost.Tracking.Infrastructure --context TrackingDbContext
```

3. Send the requests in [`ApexPost.Api/ApexPost.Api.http`](ApexPost.Api/ApexPost.Api.http) from Visual Studio, Rider or the VS Code REST Client. Every endpoint has a success request and a failure request.

## Week 4 Part C: OpenAPI, Swagger UI, versioning and the client SDK

### OpenAPI, the built-in generator and Swagger UI

These are three different things:

- **The OpenAPI specification** is a standard, language-neutral JSON or YAML format that describes an HTTP API: its paths, operations, parameters, request and response schemas, status codes and security. It is a contract, not code. Our documents are `/openapi/v1.json` and `/openapi/v2.json`, and copies are committed in [`contracts/`](contracts).
- **The built-in .NET generator** (`Microsoft.AspNetCore.OpenApi`, `AddOpenApi()` and `MapOpenApi()`) writes those documents at run time. It reads the controllers, routes, DTOs, `[ProducesResponseType]`, `[Tags]`, `[EndpointSummary]`, route names (which become the operationIds) and the XML comments. The transformers in `ApexPost.Api/Hosting/OpenApiDocuments.cs` add the title, description, version, contact, deprecation flags and the tracking number pattern.
- **Swagger UI** (`Swashbuckle.AspNetCore.SwaggerUi`) is only a web page that displays an OpenAPI document and lets you call the API with "Try it out". It generates nothing. Here it reads the two built-in documents and offers them in a dropdown as "v2" and "v1 (deprecated)". Full Swashbuckle generation is not added, so there is one generator and one source of truth.

The documents and Swagger UI are mapped only in Development, at `https://localhost:7180/openapi/v2.json` and `https://localhost:7180/swagger`. Hiding them elsewhere is not a security measure: the endpoints are protected the same way whether or not they are documented.

### Versions

`/api/v1/...` is the Part B contract, unchanged and deprecated. `/api/v2/...` nests the recipient, adds `latestScan` and returns the status as a name. [`VERSIONING.md`](VERSIONING.md) lists every change, whether it is breaking, and the v1 sunset plan.

### The generated client

`ApexPost.Client` is generated by NSwag from `contracts/v2.json`, using the settings in [`ApexPost.Client/nswag.json`](ApexPost.Client/nswag.json). After a contract change, regenerate it from the repository root:

```bash
dotnet tool restore
```

```bash
dotnet nswag run ApexPost.Client/nswag.json
```

Never edit `ApexPost.Client/Generated/` by hand. The JSON settings are in the partial class `ApexPostClient.cs`, and `ApexPostClientExtensions.cs` adds `FindShipmentAsync` (null on 404) and `Describe` for error messages.

`ApexPost.Client.Consumer` registers the client with `AddHttpClient<IApexPostClient, ApexPostClient>`, creates a shipment, fetches it, uploads two scans and handles a 404 through `ApexPostApiException<ProblemDetails>`. Start the API, then:

```bash
dotnet run --project ApexPost.Client.Consumer
```

It calls `https://localhost:7180`, so the development certificate must be trusted (`dotnet dev-certs https --trust`). The client never turns certificate checks off.

Screenshots of Swagger UI for both versions, the Authorize dialog, and the 201, 400, 401 and 409 responses are in [`docs/week4-partC`](docs/week4-partC).

## Week 4 Part D: security and the courier app

### Securing the API

The API needs a JWT bearer token for every shipment and scan route, with roles for writes, and the read-only partner route takes an `X-Api-Key` header instead. The design, the OAuth 2.0 flows and the HTTPS/HSTS reasoning are in [`SECURITY.md`](SECURITY.md). Before you run the API, set the two secrets. Neither is in source control:

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)" --project ApexPost.Api
```

```bash
dotnet user-secrets set "PartnerApiKeys:Keys:DemoPartner" "$(openssl rand -hex 24)" --project ApexPost.Api
```

PowerShell versions of the two commands above (there is no `openssl` or `$(...)` on a plain Windows shell). They use the cryptographic random number generator and work in both Windows PowerShell 5.1 and PowerShell 7:

```powershell
$key = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($key)
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($key)) --project ApexPost.Api
```

```powershell
$partner = New-Object byte[] 24; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($partner)
dotnet user-secrets set "PartnerApiKeys:Keys:DemoPartner" (($partner | ForEach-Object { $_.ToString('x2') }) -join '') --project ApexPost.Api
```

In Development only, and only because `appsettings.Development.json` sets `Jwt:AllowDevelopmentTokens`, `POST /dev/token` issues a test token to a caller on the same machine, for example `{"roles":["Courier"],"courierId":"CR-1001"}`. In Swagger UI, paste it into **Authorize**. [`ApexPost.Api/ApexPost.Api.http`](ApexPost.Api/ApexPost.Api.http) starts by requesting tokens, and ends with the 401, 403, invalid API key and valid token cases.

### The courier app

A 4½-minute demo video of login, offline scans, automatic sync, 401/403 and the circuit breaker is in [`docs/week4-partD/demo.mp4`](docs/week4-partD/demo.mp4), with a description in [`docs/week4-partD/README.md`](docs/week4-partD/README.md).

`ApexPost.Courier` is a .NET MAUI app with four screens: Login, New Scan, Pending Scans and Sync Status. Its logic is in `ApexPost.Courier.Core`, which needs no MAUI workload and is tested by `ApexPost.Courier.Core.Tests`:

| Part | Where |
|---|---|
| Typed client `ApexApiClient`, registered with `AddHttpClient<ApexApiClient>()` | `ApexPost.Courier.Core/Api`, `ApexPost.Courier/MauiProgram.cs` |
| `AuthHeaderHandler` (bearer token from SecureStorage, sign-out on 401) | `ApexPost.Courier.Core/Auth` |
| Retry, circuit breaker and timeouts with logging | `ApexPost.Courier.Core/Resilience`, explained in [`RESILIENCE.md`](RESILIENCE.md) |
| Source-generated `ApexJsonContext` and shared JSON options | `ApexPost.Courier.Core/Api/ApiDtos.cs` |
| Batch sync of Pending scans (20 at a time) on reconnect, and the real uploader that replaces the Part A fake | `ApexPost.Courier.Core/Sync` |
| SecureStorage, Connectivity, pages and the `#if DEBUG` development certificate handler | `ApexPost.Courier` |

The app needs the MAUI workload, so it is in its own solution, `ApexPost.Mobile.slnx`:

```bash
dotnet workload install maui-android
```

```bash
dotnet build ApexPost.Courier -f net10.0-android -p:CourierPlatforms=net10.0-android
```

The API base URLs are in `ApexPost.Courier/appsettings.json`: `https://10.0.2.2:7180` on the Android emulator and `https://localhost:7180` on Windows and elsewhere. Start the API with the `https` profile first.

**Run the app and sign in.** With the API running, start the app on an emulator (`dotnet build ApexPost.Courier -f net10.0-android -p:CourierPlatforms=net10.0-android -t:Run`, or press Run in Visual Studio with the Android emulator selected). On the Login screen in a Debug build, enter a courier id such as `CR-1001` and tap **Get development token (Debug build)**: the app asks the API's Development-only `/dev/token` for a Courier token and keeps it in SecureStorage. Or paste a token from `POST /dev/token` or `dotnet user-jwts`. A Release build has no development sign-in and needs a token from a real identity provider. Then add scans on New Scan, turn on airplane mode to see them stay Pending, and turn it off to watch Sync Status upload them.

`CourierAppEndToEndTests` in `ApexPost.Api.Tests` runs the app's view models, client, handlers, resilience pipeline and Couchbase Lite store against the real API pipeline. It covers login, 25 scans saved offline, the automatic upload on reconnect, a resent scan stored only once, a refused scan marked Failed, and another courier's token refused.

## Solution layout

- `ApexPost.Domain`: entities, rules, events, enums, value objects. It references nothing.
- `ApexPost.Application`: repository, unit-of-work and query contracts, DTOs, use cases, pricing.
- `ApexPost.Infrastructure`: EF Core DbContext, configurations, migrations, repositories, query services, simulated courier service, cache, notifications.
- `ApexPost.Web`: MVC controllers/views, authentication, middleware, filters, composition root.
- `ApexPost.Test`: unit, model, MVC and SQL Server integration tests.
- `ApexPost.Courier.Offline`: Week 4 Part A, the courier's offline store (see "Week 4 Part A" above). It does not reference any Week 3 project.
- `ApexPost.Courier.Offline.Harness`: the console test harness for it.
- `ApexPost.Courier.Offline.Tests`: its tests.
- `ApexPost.Tracking`: Week 4 Part B, the shipment and scan domain and `ShipmentService`. It references the Week 3 Application project for `IBranchRepository`.
- `ApexPost.Tracking.Infrastructure`: `TrackingDbContext`, its migrations and the EF store.
- `ApexPost.Api`: the Web API.
- `ApexPost.Api.Tests`: domain, service, HTTP, versioning, contract and SDK tests, and SQL Server tests that run when `APEXPOST_TEST_SQLSERVER` is set.
- `ApexPost.Client`: the NSwag-generated v2 client, with its hand-written partial class and extensions.
- `ApexPost.Client.Consumer`: the console program that uses it.
- `ApexPost.Courier.Core`: Week 4 Part D, the courier app's logic: typed API client, handlers, resilience, JSON, sync and view models.
- `ApexPost.Courier.Core.Tests`: its tests.
- `ApexPost.Courier`: the .NET MAUI courier app (in `ApexPost.Mobile.slnx`, because it needs the MAUI workload).

See `DESIGN.md` for the architecture and ER diagrams, middleware sequence, filters, ADRs and security decisions, and `PERFORMANCE.md` for index evidence.
