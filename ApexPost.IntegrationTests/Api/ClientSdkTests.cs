using ApexPost.Client;
using ApexPost.Client.Consumer;
using ApexPost.Testing.Tracking;
using ApexPost.Testing.Api;

namespace ApexPost.IntegrationTests.Api;

/// <summary>The generated v2 client and the console consumer, run against the real API pipeline in memory.</summary>
public sealed class ClientSdkTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task ConsumerRun_AgainstInProcessApi_CompletesEveryStep()
    {
        // Arrange
        var output = new StringWriter();
        var demo = new ConsumerDemo(new ApexPostClient(_factory.CreateClient()), _factory.Clock);

        // Act
        var exitCode = await demo.RunAsync(output, CancellationToken.None);

        // Assert
        Assert.Equal(0, exitCode);

        // Act
        var lines = output.ToString();

        // Assert
        Assert.Contains("Uploaded 2 scans: 2 accepted, 0 duplicates skipped, 0 rejected", lines);
        Assert.Contains("Latest   InTransit at Sydney hub", lines);
        Assert.Contains("Handled  404: Shipment not found.", lines);
    }

    [Fact]
    public async Task GeneratedClient_V2Responses_ReadsShapesAndRaisesTypedProblemDetails()
    {
        // Arrange
        var client = new ApexPostClient(_factory.CreateClient());

        // Act
        var created = await client.CreateShipmentAsync(new CreateShipmentRequest
        {
            TrackingNumber = "APX-20000001",
            OriginBranchCode = "B001",
            RecipientName = "Mia Fictional",
            RecipientPhone = "+61 2 5550 1234",
            RecipientAddress = "12 Example Lane",
            City = "Sydney",
            WeightKg = 2.5,
        });

        // Assert
        Assert.Equal(ShipmentStatus.Created, created.Status);
        Assert.Equal("Sydney", created.Recipient.City);
        Assert.Null(await client.FindShipmentAsync("APX-99999999"));

        var invalid = await Assert.ThrowsAsync<ApexPostApiException<ValidationProblemDetails>>(() =>
            client.CreateShipmentAsync(new CreateShipmentRequest { TrackingNumber = "bad", OriginBranchCode = "B001", RecipientName = "M", RecipientPhone = "x", RecipientAddress = "1", City = "S", WeightKg = 0 }));
        Assert.Equal(400, invalid.StatusCode);
        Assert.Contains("trackingNumber", invalid.Result.Errors!.Keys);

        var conflict = await Assert.ThrowsAsync<ApexPostApiException<ProblemDetails>>(() =>
            client.UpdateShipmentStatusAsync("APX-20000001", new UpdateStatusRequest { Status = ShipmentStatus.Delivered }));
        Assert.Equal(409, conflict.StatusCode);
        Assert.Contains("409 Invalid status transition", conflict.Describe());
    }
}
