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

[`demo.mp4`](demo.mp4) (4 min 37 s, no audio, captions on screen) shows the Debug courier app running on an Android emulator against the real API over HTTPS with SQL Server:

1. **Login**: the app asks the Development-only `/dev/token` for a Courier token and keeps it in SecureStorage.
2. **Scans saved in airplane mode**: two scans are saved on the device as Pending (Couchbase Lite), the "Server unavailable – working offline" banner shows, and Pending reads 2.
3. **Automatic sync after reconnecting**: airplane mode off, Pending drops to 0, Sync Status shows "2 synced", and the API's scan list for the shipment shows both scans.
4. **401 and 403**: real responses from the API for no token, an invalid token, a courier token on an Admin-only route, a scan under another courier's id, and a wrong partner key, each as ProblemDetails.
5. **Circuit breaker**: with the API stopped, a saved scan stays Pending, the retries fail, and Sync Status shows the circuit **Open**. After the API is started again and the 30 second open period passes, Sync now closes the circuit, Pending drops to 0 and the scan reaches the server.

It was recorded with `adb screenrecord` while a script drove the emulator (airplane mode, taps, and stopping and starting the API), then assembled with ffmpeg. Nothing in it is drawn: the phone is the real app, and the 401/403 and server lists are the API's actual replies.
