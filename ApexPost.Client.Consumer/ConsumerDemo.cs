using ApexPost.Client;

namespace ApexPost.Client.Consumer;

/// <summary>Creates a shipment, fetches it, uploads two scans and shows how a 404 arrives.</summary>
public sealed class ConsumerDemo(IApexPostClient api, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<int> RunAsync(TextWriter output, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Create. A random tracking number keeps repeated runs from colliding (fictional data only).
            var trackingNumber = $"APX-{Random.Shared.Next(70_000_000, 80_000_000)}";
            var created = await api.CreateShipmentAsync(new CreateShipmentRequest
            {
                TrackingNumber = trackingNumber,
                OriginBranchCode = "B001",
                RecipientName = "Mia Fictional",
                RecipientPhone = "+61 2 5550 1234",
                RecipientAddress = "12 Example Lane",
                City = "Sydney",
                WeightKg = 2.5,
            }, cancellationToken);
            output.WriteLine($"Created  {created.TrackingNumber}: {created.Status}, to {created.Recipient.Name} in {created.Recipient.City}");

            // 2. Fetch.
            var fetched = await api.GetShipmentAsync(trackingNumber, cancellationToken);
            output.WriteLine($"Fetched  {fetched.TrackingNumber}: {fetched.Status}, latest scan {fetched.LatestScan?.ScanType.ToString() ?? "none"}");

            // 3. Upload two scans recorded offline, as a courier device would.
            var now = _clock.GetUtcNow();
            var upload = await api.UploadScansAsync(trackingNumber, new ScanUploadRequest
            {
                Scans =
                [
                    Scan(ScanType.Pickup, "Sydney depot", now.AddMinutes(-2)),
                    Scan(ScanType.InTransit, "Sydney hub", now.AddMinutes(-1)),
                ],
            }, cancellationToken);
            output.WriteLine($"Uploaded 2 scans: {upload.Accepted} accepted, {upload.DuplicatesSkipped} duplicates skipped, {upload.Rejected} rejected");

            var withScans = await api.GetShipmentAsync(trackingNumber, cancellationToken);
            output.WriteLine($"Latest   {withScans.LatestScan?.ScanType} at {withScans.LatestScan?.Location}");

            // 4. An unknown tracking number comes back as the generated exception type with the ProblemDetails.
            try
            {
                await api.GetShipmentAsync("APX-00000000", cancellationToken);
                output.WriteLine("Unexpected: APX-00000000 exists.");
                return 1;
            }
            catch (ApexPostApiException<ProblemDetails> notFound) when (notFound.StatusCode == 404)
            {
                output.WriteLine($"Handled  404: {notFound.Result.Title}. {notFound.Result.Detail}");
            }

            return 0;
        }
        catch (ApexPostApiException exception)
        {
            output.WriteLine($"The API refused a request: {exception.Describe()}");
            return 1;
        }
        catch (HttpRequestException exception)
        {
            output.WriteLine($"Could not reach the API: {exception.Message}. Is it running?");
            return 1;
        }
    }

    private static ScanUploadItem Scan(ScanType type, string location, DateTimeOffset at) => new()
    {
        ScanId = $"scan::{Guid.NewGuid():D}",
        ScanType = type,
        CourierId = "CR-1001",
        Location = location,
        ScannedAtUtc = at,
        Notes = "Uploaded by the console consumer",
    };
}
