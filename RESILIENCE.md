# Courier app resilience

Couriers work on weak mobile networks. Every API call from the courier app goes through one Polly pipeline, built with `Microsoft.Extensions.Http.Resilience` in `ApexPost.Courier.Core/Resilience/ApexResilience.cs` and attached to the typed `ApexApiClient` with `AddHttpClient<ApexApiClient>().AddApexResilience()`. The values are in `ApexResilienceOptions`, and `ResilienceTests` pins them.

From the outside in:

```
overall timeout (60 s)
  └─ retry (3 retries, exponential backoff with jitter, idempotent requests only)
       └─ circuit breaker (opens at 50% failures over 30 s, at least 4 calls; stays open 30 s)
            └─ per-attempt timeout (10 s)
                 └─ AuthHeaderHandler sits outside the pipeline, so every attempt carries the token
```

## Values and reasons

| Setting | Value | Why |
|---|---|---|
| Retries | 3 (4 attempts at most). The brief says "Retry: 3 attempts"; this reads it as three retries after the first try | Mobile failures are usually short: a cell handover, a tunnel, a server restarting. Three retries ride out most of them. More would mostly delay the moment the scan stays safely Pending and the courier moves on. |
| Backoff | Exponential from 1 s with jitter (about 1, 2 and 4 s) | Exponential gives the server and the network room to recover instead of hammering them. Jitter stops a fleet of devices that lost coverage together from retrying in lockstep when it comes back. |
| What is retried | Network errors (`HttpRequestException`), attempt timeouts, HTTP 408 and 5xx | These may succeed later. A 4xx never is: 400, 401, 403, 404 and 409 fail the same way every time, so retrying only wastes battery and data. |
| Which requests | GET, PUT, DELETE, HEAD, and requests marked idempotent | A retry repeats the request, so it must be safe to repeat. A plain POST is not retried (the `/dev/token` request, for example). |
| Per-attempt timeout | 10 s | Enough for a batch of 20 scans on a slow 3G link. A request still waiting after 10 s is almost always stuck, and starting a fresh attempt is the quickest way out. |
| Overall timeout | 60 s | Four 10 s attempts plus about 7 s of backoff fit. It caps how long the Sync screen can wait, so the courier is never stuck behind a dead network. |
| Circuit breaker | 50% failures in 30 s, at least 4 calls; open for 30 s | When the server is down, every call would otherwise wait out its timeouts and retries. Open, the circuit fails calls at once and the app shows "Server unavailable – working offline". Four calls is one fully failed request with its retries, so one clearly broken call is enough to open it. After 30 s one trial call is let through (half-open). If it succeeds the circuit closes, and if not it opens again. |

## Why the scan upload is safe to retry

`POST /api/v2/shipments/{trackingNumber}/scans` is a POST, which is not idempotent in general. `ApexApiClient.UploadScansAsync` marks it idempotent (`ApexApiClient.Idempotent`) because the operation is idempotent by design:

- Every scan carries its Part A id, `scan::{Guid}`, made once on the device when the scan was recorded and never changed.
- The server keeps that id as the primary key of `tracking.Scans` and skips any id it already has, reporting it in `duplicatesSkipped` (Part B, B6). Even two uploads racing each other store each scan once; a SQL Server test runs four in parallel.

So if the reply to an upload is lost and the pipeline, or the next sync run, sends the same batch again, nothing is stored twice. The app counts a duplicate as Synced. This is the same at-least-once delivery the Part A outbox design relies on.

## What the app does in each state

| Situation | Result | Scans |
|---|---|---|
| Device offline (`Connectivity` says no internet) | No call is made | Saved locally as Pending. The banner shows. |
| Network error or 5xx, retries exhausted | `ApiFailureKind.Unreachable` or `ServerError` | Stay Pending. This sync run stops and the next one tries again. |
| A reply the app cannot read (a captive-portal page on public Wi-Fi answers 200 with HTML) | `ApiFailureKind.Malformed`, treated like a network failure | Stay Pending. The run stops, and the scans go up when the network is real. |
| Circuit open | `ApiFailureKind.CircuitOpen`, at once | Stay Pending. The banner "Server unavailable – working offline" shows on every tab. |
| 401 | Token cleared, Login page | Stay Pending until the courier signs in again. |
| 400, 403, 404 | `ApiFailureKind.Rejected` with the ProblemDetails message | Marked Failed, with the reason on the Sync Status screen. "Retry failed scans" on that screen moves them back to Pending and sends them again, for example after a back-office user creates the missing shipment. |
| 200 with rejections | Per scan | Rejected scans Failed with the server's reason. Accepted and duplicate scans Synced. |
| Back online (`Connectivity.ConnectivityChanged`) | `ScanSyncService` starts a run | Pending scans go up in batches of 20, oldest first. |

## Edits and deletes while an upload runs

After the server accepts a scan, `ScanSyncService` reads the scan again before marking it Synced. If it was edited meanwhile it stays Pending and is sent again on the next run. The server already has that id, so it reports it as a duplicate and keeps the version it first received: an edit after upload never changes the server's copy. A correction must be recorded as a new scan, which is the courier's call. The app has no edit screen, so this only matters for code that calls `Update` directly. If it was deleted, it stays deleted. The Part A `OutboxService` has the same rule for failures that may pass: an uploader that throws `TransientUploadException` leaves the scan Pending and stops the run, instead of marking it Failed.

## Logging

Every retry is logged as a warning, for example "Retry 2 of 3 for POST /api/v2/shipments/APX-10000001/scans in 2.14 s, after HTTP 503". Every circuit change is logged: opened (an error, with the break duration and the cause), half-open and closed. Logs never contain a token, an API key or a request body.
