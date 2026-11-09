# Week 4 Part A: evidence

Everything here came from running the real harness (`ApexPost.Courier.Offline.Harness`) against a fresh Couchbase Lite database. Nothing was typed in by hand.

| File | What it shows |
|---|---|
| `query-1-scans-for-a-tracking-number.png` | Option 2. All scans for a tracking number, ordered by `scannedAtUtc` (SQL++ with `$trackingNumber`) |
| `query-2-pending-scans.png` | Option 3. All scans where `syncStatus` is Pending (SQL++ with `$syncStatus`) |
| `query-3-scans-per-scan-type.png` | Option 1. After each save, the number of scans per `scanType` for that courier (SQL++ `GROUP BY` with `$courierId`) |
| `query-4-sync-and-failed-scan.png` | Option 4. The outbox marks scans Synced or Failed, then List Pending is empty and the Failed scan is still there |
| `query-plans.png`, `query-plans.txt` | The `--explain` output: which index each statement uses |
| `listener-log.png`, `listener-log.txt` | The change listener: the ids of changed documents, the live pending count, and the contents of `listener.log` |
| `query-results.txt` | The complete transcript behind the four query images |

The PNG files are drawings of the captured console text, not captures of a terminal window. The `.txt` files are the raw output. The first line of each is Couchbase Lite's own start-up message, which also shows the version in use (4.1.2).

To make them again, build the solution, compile `tools/render.swift` (macOS) and run `tools/make_evidence.py`:

```bash
dotnet build ApexPost.slnx -c Release
swiftc -O docs/week4-partA/tools/render.swift -o /tmp/render
python3 docs/week4-partA/tools/make_evidence.py ApexPost.Courier.Offline.Harness/bin/Release/net10.0/ApexPost.Courier.Offline.Harness.dll docs/week4-partA /tmp/render
```
