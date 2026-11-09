# Offline design note: Week 4, Part A

The courier's device keeps its scans in an embedded Couchbase Lite database, `apexpost_courier`. Three decisions sit behind that.

## 1. Why the app saves locally first

A scan records something that has just happened, such as a parcel handed over at a door. It cannot be redone later, and the courier often has no signal when it happens. If saving needed the network, a dropped connection would lose or delay the record, or make the courier retry at the door.

So every new scan is written to the device first, with `syncStatus = Pending`. The write is local, it does not wait for the server, and queries and change notifications work with no network. Uploading is a separate job. The `OutboxService` reads the Pending scans, oldest first, passes each to an `IShipmentUploader`, and records the answer as `Synced` or `Failed`. A scan the server rejects stays on the device as Failed, so nothing is lost silently, and the Pending count shows how much is not yet safe on the server.

Delivery is at least once. If the app stops after an upload but before the scan is marked Synced, the scan is sent again. The document id, `scan::{guid}`, is therefore the idempotency key: the server must treat a second upload of the same id as the same scan.

## 2. Conflicts: last write wins, or the server decides

| | Last write wins | The server decides |
|---|---|---|
| Good | Simple, and needs no round trip | One system of record, with the rules in one place |
| Bad | Phone clocks drift, so a stale device can overwrite newer data | The device must wait for the server's answer |

**The server decides.** The SQL Server database from Week 3 is the system of record. A scan is a fact that a device creates once with a unique id, and afterwards only its `syncStatus` changes, so real conflicts are rare:

- The same id arriving twice is the same scan, and the server ignores the repeat.
- A scan the server rejects stays Failed on the device for a person to review.
- If office staff correct a scan on the server, the correction wins and the device takes the server's copy at its next pull.

Last write wins is used only on the device, for fields the device alone owns. `UpdateSyncStatus` reads the latest copy, changes only the status, and saves with `FailOnConflict`, retrying if the document changed in between, so an edit made during an upload is kept. A full `Update` is last-write-wins because the caller supplies the whole scan.

## 3. Why an embedded database must not be shared by several processes

Couchbase Lite is a library inside the app's own process, not a server. Its locks, caches and change notifications live in that process's memory. A second process opening the same folder would have its own caches and locks, which cannot see the first process's state. That means stale reads, listeners that never hear the other process's writes, writers blocking each other, and a risk of damage during maintenance such as compaction. The library is built for one owning process per database folder.

So the app uses one `CourierDatabase` per process and disposes it when finished, and the harness must not be pointed at a folder another program is using. Nothing in the code stops a second process from opening the folder. On a phone the operating system gives each app its own sandbox, so this does not arise. If several programs ever needed the same scans, one process would own the database and the others would call it, or all of them would sync through the server.

Part D added one optional field, `recipientSignatureName`. It is written only on a Delivered scan that has a signature, because the server needs one for a delivery. Every other scan keeps exactly the seven fields above. The fake uploader is replaced in the app by the real API call (`ApiShipmentUploader`), which sends scans in batches of 20 when the device comes back online.
