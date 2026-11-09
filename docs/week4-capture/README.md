# Capturing the Week 4 evidence

Two scripts that produce real evidence from the running code. Run both from the repository root, in macOS Terminal (not an IDE terminal).

**Before you start, once:** allow Terminal to record the screen (System Settings → Privacy & Security → Screen & System Audio Recording → Terminal), then quit and reopen Terminal.

## Part A: screenshots of the harness

```bash
python3 docs/week4-capture/partA_screenshots.py
```

It builds `ApexPost.Courier.Offline.Harness` and runs it seven times on a cleared screen. Each time it types the answers into the harness through a pseudo-terminal, so the window shows an ordinary keyboard session. Then it saves a picture of the Terminal window with `screencapture`. Leave the window alone while it runs (about two minutes).

| File in `docs/week4-partA/screenshots/` | Shows |
|---|---|
| `1-add-scan-count-per-scan-type.png` | Add Scan, with the count per `scanType` for the courier (SQL++ `GROUP BY`, `$courierId`) and the listener lines |
| `2-find-by-tracking-number.png` | Find by Tracking Number, ordered by `scannedAtUtc` (`$trackingNumber`) |
| `3-list-pending.png` | List Pending (`$syncStatus`) |
| `4-simulate-sync-then-pending-and-failed.png` | Simulate Sync, then List Pending is empty and the `[fail]` scan is Failed |
| `5-listener-while-adding.png` | The change listener: changed document ids and the live pending count |
| `6-listener-sync-delete-and-log-file.png` | The listener during sync and delete, then `listener.log` |
| `7-query-plans.png` | `--explain`: the value index each statement uses |
| `transcript.txt` | Everything the window showed, as text |

When they look right, point `docs/week4-partA/README.md` at these files instead of the drawn images, and delete the drawn ones.

## Part D: the demo video

Start SQL Server (Docker) and the Android emulator, stop any API you have running, then:

```bash
python3 docs/week4-capture/partD_demo.py --deploy-app
```

The script starts the real API and installs the Debug app on the emulator. It then asks you to start the screen recording (Cmd+Shift+5 → Record Selected Portion around the emulator and Terminal; turn on the microphone under Options to narrate). For each scene it prints what to do in the emulator and a line to say, then waits for Return:

1. What this is
2. Login with a development token
3. Two scans saved in airplane mode, Pending count 2
4. Airplane mode off: automatic upload, Pending 0. The script shows the scans arriving on the server.
5. 401 and 403. The script sends the requests and shows each status and ProblemDetails message.
6. The script stops the API. A scan is saved, retries fail and the circuit opens (banner, Sync Status: Open).
7. The script restarts the API and counts down 30 seconds. Tap Sync now: the circuit closes, Pending 0.

Aim for 3–5 minutes. Trim the start and end in QuickTime (Edit → Trim). Upload it (an unlisted YouTube video or a shared Drive file) and link it from `README.md` and `docs/week4-partD/README.md`.

Tokens come from the API's Development-only `/dev/token`. The partner key is read from your user secrets and is never printed. HTTPS is verified against the ASP.NET Core development certificate, exported for the run and deleted afterwards.
