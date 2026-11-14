# Mutation 2: the server stops checking that scans belong to the signed-in courier

## The deliberate break

```diff
diff --git a/ApexPost.Tracking/Application/ShipmentService.cs b/ApexPost.Tracking/Application/ShipmentService.cs
index ac3ba68..8ccea05 100644
--- a/ApexPost.Tracking/Application/ShipmentService.cs
+++ b/ApexPost.Tracking/Application/ShipmentService.cs
@@ -154,7 +154,7 @@ public sealed class ShipmentService(IShipmentStore store, IBranchRepository bran
 
         // A courier uploads only the scans they made. One scan under another courier id refuses the whole batch,
         // because it means the device or the token is wrong, not that one scan is bad.
-        var foreign = scans.FirstOrDefault(scan => !string.Equals(scan.CourierId, uploadingCourierId, StringComparison.Ordinal));
+        IncomingScan? foreign = null; // MUTATION: the courierId ownership check is gone
         if (string.IsNullOrWhiteSpace(uploadingCourierId) || foreign is not null)
         {
             return new ServiceError(
```

## The tests with the break in place

```text
[xUnit.net 00:00:02.12]     A courier can only upload scans under their own courier id [FAIL]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Forbidden
Actual:   OK
 -> error: Assert.Equal() Failure: Values differ
 Expected: Forbidden
 Actual:   OK (0.0s)
Failed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 1 s - ApexPost.Specs.dll (net10.0)
```

## After `git checkout -- ApexPost.Tracking/Application/ShipmentService.cs` restored the code

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 1 s - ApexPost.Specs.dll (net10.0)
```
