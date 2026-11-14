# Mutation 3: the Admin-only rule is removed from DELETE

## The deliberate break

```diff
diff --git a/ApexPost.Api/Controllers/V2/ShipmentsController.cs b/ApexPost.Api/Controllers/V2/ShipmentsController.cs
index 178ceb0..156031c 100644
--- a/ApexPost.Api/Controllers/V2/ShipmentsController.cs
+++ b/ApexPost.Api/Controllers/V2/ShipmentsController.cs
@@ -120,7 +120,7 @@ public sealed class ShipmentsController(IShipmentService shipments) : Controller
     /// <response code="409">The shipment has moved past Created.</response>
     [EndpointSummary("Delete a shipment")]
     [HttpDelete("{trackingNumber:trackingNumber}", Name = "DeleteShipment")]
-    [Authorize(Roles = ApexPostRoles.Admin)]
+    // MUTATION: [Authorize(Roles = ApexPostRoles.Admin)] removed
     [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
     [ProducesResponseType(StatusCodes.Status204NoContent)]
     [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
```

## The tests with the break in place

```text
[xUnit.net 00:00:01.54]     A courier cannot remove a shipment [FAIL]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: Forbidden
Actual:   NoContent
 -> error: Assert.Equal() Failure: Values differ
 Expected: Forbidden
 Actual:   NoContent (0.0s)
Failed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 1 s - ApexPost.Specs.dll (net10.0)
```

## After `git checkout -- ApexPost.Api/Controllers/V2/ShipmentsController.cs` restored the code

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 1 s - ApexPost.Specs.dll (net10.0)
```
