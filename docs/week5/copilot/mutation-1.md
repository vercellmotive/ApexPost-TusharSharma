# Mutation 1: the outbox marks every scan Synced, even a rejected one

## The deliberate break

```diff
diff --git a/ApexPost.Courier.Offline/Sync/OutboxService.cs b/ApexPost.Courier.Offline/Sync/OutboxService.cs
index 94276db..856d737 100644
--- a/ApexPost.Courier.Offline/Sync/OutboxService.cs
+++ b/ApexPost.Courier.Offline/Sync/OutboxService.cs
@@ -47,7 +47,7 @@ public sealed class OutboxService(IScanRepository repository, IShipmentUploader
 
             // Only the status changes, so an edit made to the scan while it was uploading is kept.
             // False means the scan was deleted meanwhile, and it must not come back.
-            var status = result.Succeeded ? SyncStatus.Synced : SyncStatus.Failed;
+            var status = SyncStatus.Synced; // MUTATION: a rejected scan is marked Synced
             if (!repository.UpdateSyncStatus(scan.Id, status))
             {
                 skipped.Add(scan.Id);
```

## The tests with the break in place

```text
[xUnit.net 00:00:00.18]     ApexPost.UnitTests.Offline.OutboxServiceTests.SyncPendingAsync_RejectedUpload_MarksScanFailedWithError [FAIL]
[xUnit.net 00:00:00.19]     ApexPost.UnitTests.Offline.OutboxServiceTests.SyncPendingAsync_UploaderReturnsNoResult_MarksScanFailedInsteadOfCrashing [FAIL]
  Error Message:
Expected invocation on the mock once, but was 0 times: repository => repository.UpdateSyncStatus("scan::0384b637-ab4a-4e08-929c-0d6893732bc8", SyncStatus.Failed)
  Error Message:
Expected invocation on the mock once, but was 0 times: repository => repository.UpdateSyncStatus("scan::9f39fda6-33ee-4a73-b40a-cdc3eaf9fbef", SyncStatus.Failed)
Failed!  - Failed:     2, Passed:     6, Skipped:     0, Total:     8, Duration: 72 ms - ApexPost.UnitTests.dll (net10.0)
```

## After `git checkout -- ApexPost.Courier.Offline/Sync/OutboxService.cs` restored the code

```text
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 62 ms - ApexPost.UnitTests.dll (net10.0)
```
