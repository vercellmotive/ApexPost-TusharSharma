# Week 4 Part D evidence

| File | Shows |
|---|---|
| [`http-check.txt`](http-check.txt) | All 60 requests in `ApexPost.Api.http` sent to the secured API by [`../week4-partC/tools/check-http.py`](../week4-partC/tools/check-http.py). Each returns the status in its title, including the 401 (no token, invalid token, no or wrong API key), the 403 (wrong role, another courier's id) and the valid-token and valid-key cases. |
| [`app-new-scan-saved.png`](app-new-scan-saved.png) | The courier app running on the Android emulator after "Save scan": the scan is saved on the device as Pending, then uploaded over HTTPS to the API at `https://10.0.2.2:7180`. |
| [`app-offline-banner.png`](app-offline-banner.png) | The same app in airplane mode: the orange banner "Server unavailable – working offline" shows, and scans are still saved on the device. |

On the emulator run, the API received the scan from the app: `GET /api/v2/shipments/APX-10000001/scans` listed it with `courierId` CR-1001, the id the app's token carries.

## What is verified by tests instead

The reconnect upload, the 20-scan batches, the retry and circuit breaker behaviour, the 401 sign-out and the courierId rule are verified by automated tests rather than screenshots:

- `ApexPost.Courier.Core.Tests` (65 tests): retries on 408, 5xx and network errors, never on a 4xx, per-attempt and overall timeouts, the circuit opening and closing with its log lines, a 401 clearing the token without logging it, malformed and unknown JSON, batching by 20, scans staying Pending while the server is down, and the sync after `ConnectivityChanged`.
- `ApexPost.Api.Tests` `CourierAppEndToEndTests`: login, 25 scans saved offline, automatic upload on reconnect, a resent scan stored once, a refused scan marked Failed, and another courier's token refused.

## Demo video

The 3–5 minute video is recorded by the student, because it shows their own machine. Suggested order, with the API started by `dotnet run --project ApexPost.Api --launch-profile https`:

1. Login: tap "Get development token". 2. Turn on airplane mode, save two or three scans, and open Pending (the count follows the change listener). 3. Turn airplane mode off and watch Pending drop to zero and Sync Status show the last sync. 4. In Swagger UI or `ApexPost.Api.http`, show a 401 (no token) and a 403 (courier token on DELETE). 5. Stop the API, save a scan with the device online so sync fails, and show the orange banner and the circuit state "Open" on Sync Status, then restart the API and show it recover after about 30 seconds.
