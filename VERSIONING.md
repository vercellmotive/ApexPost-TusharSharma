# ApexPost API versioning

The version is part of the URL: `/api/v1/shipments` and `/api/v2/shipments`. Both versions call the same `IShipmentService`, so the business rules are identical. Only the response DTOs and their mapping differ (`Contracts/` for v1, `Contracts/V2/` for v2).

Every response reports the versions in two headers, `api-supported-versions: 2.0` and `api-deprecated-versions: 1.0`, because `ReportApiVersions` is on.

## Contracts

The OpenAPI document of each version is committed in [`contracts/v1.json`](contracts/v1.json) and [`contracts/v2.json`](contracts/v2.json). `ContractTests` compares the documents the API serves with these files. A change that alters a contract fails the build until the file is updated on purpose and the change is listed below. To update the files after an intended change:

```bash
APEXPOST_UPDATE_CONTRACTS=1 dotnet test ApexPost.Api.Tests --filter ContractTests
```

## Changes

| Version | Change | Breaking? | Why |
|---|---|---|---|
| v1 | First contract (Part B): eight endpoints under `/api/shipments` | n/a | The shipment and scan API |
| v1 | The routes moved to `/api/v1/shipments`. The old `/api/shipments` URLs are rewritten to v1 | No | Part B clients keep working with no change. Only the canonical URL and the `Location` header now name the version |
| v1 | `Deprecation`, `Sunset` and `Link` response headers | No | New headers. Clients that ignore them see no difference |
| v2 | `recipientName`, `recipientPhone`, `recipientAddress` and `city` grouped into a nested `recipient { name, phone, address, city }` object | **Yes** | A v1 client reads `recipientName` and finds nothing, because the fields moved |
| v2 | `status` returned as a string (`"InTransit"`) instead of a number (`2`) | **Yes** | A client that parses an integer fails on a string. Names cannot change meaning if the enum is ever reordered |
| v2 | `latestScan { scanType, location, scannedAtUtc }` added, `null` before the first scan | No on its own | An added field is ignored by tolerant readers. It shipped together with the two breaking changes, so it is v2 only |
| v1, v2 | Requests, routes, status codes and validation | No | The same in both versions. A v2 client sends exactly the v1 request bodies |
| v1, v2 | Scan endpoints (`/scans`) | No | The scan contract did not change, so one controller serves both versions |

Rule of thumb: adding an optional request field, a response field, an endpoint or a header is non-breaking. Removing or renaming a field, changing its type or meaning, making an optional field required, or changing a status code is breaking and needs a new version.

## Deprecation and sunset plan for v1

| Date | Step |
|---|---|
| 2026-11-09 | v2 released. v1 marked `Deprecated = true`: its operations show as deprecated in `/openapi/v1.json`, Swagger UI labels it "v1 (deprecated)", and every v1 response carries `Deprecation: @1794182400` (2026-11-09) and `api-deprecated-versions: 1.0` |
| 2026-11-09 | Every v1 response also carries `Sunset: Wed, 30 Jun 2027 00:00:00 GMT` and a `Link` to this policy (`rel="sunset"`), so clients can find the date by themselves |
| 2027-01 | Partners and the courier app team are told directly. The generated client (`ApexPost.Client`) and the MAUI app use v2 already |
| 2027-04 | Logs are checked for remaining v1 traffic, by client, and the remaining callers are contacted |
| 2027-06-30 | Sunset. The v1 controller and DTOs are removed, and v1 URLs return 404 (later 410 Gone if needed). `contracts/v1.json` stays in Git history |

v1 gets security and data fixes until the sunset date, and no new features.
