# Week 4 Part A: evidence

These are screenshots of macOS Terminal running the real harness (`ApexPost.Courier.Offline.Harness`) against a fresh Couchbase Lite database. The answers were typed into the harness through a pseudo-terminal, and each picture is a capture of the Terminal window, not a drawing. The first line of each run is Couchbase Lite's own start-up message.

| File in `screenshots/` | What it shows |
|---|---|
| `1-add-scan-count-per-scan-type.png` | Option 1, Add Scan. After each save, the number of scans per `scanType` for that courier (SQL++ `GROUP BY` with `$courierId`), plus the change listener lines and the live pending count |
| `2-find-by-tracking-number.png` | Option 2. All scans for a tracking number, oldest first (SQL++ with `$trackingNumber`, `ORDER BY scannedAtUtc`) |
| `3-list-pending.png` | Option 3. All scans where `syncStatus` is Pending (SQL++ with `$syncStatus`) |
| `4-simulate-sync-then-pending-and-failed.png` | Option 4. The outbox marks scans Synced or Failed, then List Pending is empty and the `[fail]` scan is shown as Failed |
| `5-listener-while-adding.png` | The change listener while scans are added: the ids of the changed documents and the live pending count |
| `6-listener-sync-delete-and-log-file.png` | The listener during Simulate Sync and Delete Scan, then the contents of `listener.log` |
| `7-query-plans.png` | `--explain`: the plan for each statement and the value index it uses |
| `transcript.txt` | Everything the windows showed, as text |

[`OFFLINE-DESIGN.md`](../../OFFLINE-DESIGN.md) explains the document model, the indexes, the sync states and the conflict rules behind these results.

To make them again, run this from the repository root in macOS Terminal. The first time, allow Terminal to record the screen (System Settings, Privacy & Security, Screen & System Audio Recording). See [`../week4-capture/README.md`](../week4-capture/README.md).

```bash
python3 docs/week4-capture/partA_screenshots.py
```
