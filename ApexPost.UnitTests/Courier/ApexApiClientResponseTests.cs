using System.Net;
using System.Text;
using ApexPost.Courier.Core.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ApexPost.UnitTests.Courier;

/// <summary>
/// R5: the typed client on its own, with the network replaced by an <see cref="HttpMessageHandler"/> that answers
/// from the test. No request leaves the process. The resilience pipeline is left out on purpose, so each test sees
/// how the client itself reads one reply; retries and the circuit breaker are covered by ResilienceTests.
/// </summary>
public sealed class ApexApiClientResponseTests
{
    private const string ShipmentJson = """
        {"trackingNumber":"APX-10000001","recipient":{"name":"Mia Fictional","phone":"+61 2 5550 1234","address":"12 Example Lane","city":"Sydney"},
         "weightKg":2.5,"status":"OutForDelivery","latestScan":{"scanType":"InTransit","location":"Sydney depot","scannedAtUtc":"2026-11-09T01:30:00Z"},
         "updatedAtUtc":"2026-11-09T02:00:00Z"}
        """;

    /// <summary>A fake handler: answers every request with the response the test gives it, and keeps the request.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(HttpStatusCode status, string? body = null, string mediaType = "application/json")
            : this((_, _) => Task.FromResult(Respond(status, body, mediaType)))
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return answer(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string? body, string mediaType = "application/json") => new(status)
    {
        Content = body is null ? new ByteArrayContent([]) : new StringContent(body, Encoding.UTF8, mediaType),
    };

    private static ApexApiClient ClientFor(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.apexpost.test/") }, NullLogger<ApexApiClient>.Instance);

    [Fact]
    public async Task GetShipmentAsync_200WithJson_ReturnsTheShipmentWithStringEnums()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, ShipmentJson);
        var client = ClientFor(handler);

        // Act
        var result = await client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("APX-10000001", result.Value.TrackingNumber);
        Assert.Equal(ShipmentStatus.OutForDelivery, result.Value.Status);
        Assert.Equal(ScanKind.InTransit, result.Value.LatestScan?.ScanType);
        Assert.Equal("Sydney", result.Value.Recipient?.City);
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
        Assert.Equal("/api/v2/shipments/APX-10000001", handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetShipmentAsync_404WithProblemDetails_ReturnsRejectedWithTheServersDetail()
    {
        // Arrange
        const string problem = """{"title":"Shipment not found","status":404,"detail":"No shipment has tracking number APX-99999999."}""";
        var client = ClientFor(new StubHandler(HttpStatusCode.NotFound, problem, "application/problem+json"));

        // Act
        var result = await client.GetShipmentAsync("APX-99999999");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("No shipment has tracking number APX-99999999.", result.Message);
        Assert.False(result.IsTransient);
    }

    [Fact]
    public async Task GetShipmentAsync_404WithNoBody_ReturnsAFriendlyNotFoundMessage()
    {
        // Arrange
        var client = ClientFor(new StubHandler(HttpStatusCode.NotFound));

        // Act
        var result = await client.GetShipmentAsync("APX-99999999");

        // Assert
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
        Assert.Equal("The shipment was not found.", result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetShipmentAsync_5xx_ReturnsATransientServerError(HttpStatusCode status)
    {
        // Arrange
        var client = ClientFor(new StubHandler(status));

        // Act
        var result = await client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.ServerError, result.Failure);
        Assert.True(result.IsTransient);
        Assert.Equal("The server had a problem. Your scans are safe on this device.", result.Message);
    }

    [Fact]
    public async Task UploadScansAsync_400ValidationProblemDetails_ReturnsTheFieldErrorsAsOneMessage()
    {
        // Arrange
        const string problem = """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"scans[0].scannedAtUtc":["The scannedAtUtc field cannot be in the future."],"scans[0].location":["The location field is required."]}}
            """;
        var client = ClientFor(new StubHandler(HttpStatusCode.BadRequest, problem, "application/problem+json"));

        // Act
        var result = await client.UploadScansAsync("APX-10000001", [ScanItem()]);

        // Assert
        Assert.Equal(ApiFailureKind.Rejected, result.Failure);
        Assert.StartsWith("Please check:", result.Message);
        Assert.Contains("cannot be in the future", result.Message);
        Assert.Contains("location field is required", result.Message);
    }

    [Fact]
    public async Task GetShipmentAsync_ServerDoesNotAnswerInTime_ReturnsUnreachableAndKeepsScansSafe()
    {
        // Arrange
        // HttpClient reports its own timeout as a cancellation that the caller did not ask for.
        var client = ClientFor(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("The request timed out.", new TimeoutException()))));

        // Act
        var result = await client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Unreachable, result.Failure);
        Assert.True(result.IsTransient);
        Assert.Contains("took too long", result.Message);
    }

    [Fact]
    public async Task GetShipmentAsync_CallerCancels_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var client = ClientFor(new StubHandler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Respond(HttpStatusCode.OK, ShipmentJson));
        }));

        // Act
        Func<Task> act = () => client.GetShipmentAsync("APX-10000001", cancellation.Token);

        // Assert
        // A cancellation the caller asked for is passed on, not turned into a "server unreachable" result.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task GetShipmentAsync_NetworkFailure_ReturnsUnreachable()
    {
        // Arrange
        var client = ClientFor(new StubHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("No route to host.", null, HttpStatusCode.ServiceUnavailable))));

        // Act
        var result = await client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Unreachable, result.Failure);
        Assert.Equal("Could not reach the server. Your scans are safe on this device.", result.Message);
    }

    [Theory]
    [InlineData("<html>Sign in to the Wi-Fi</html>", "text/html")]
    [InlineData("{\"trackingNumber\":", "application/json")]
    public async Task GetShipmentAsync_200WithUnreadableBody_ReturnsMalformedInsteadOfThrowing(string body, string mediaType)
    {
        // Arrange
        var client = ClientFor(new StubHandler(HttpStatusCode.OK, body, mediaType));

        // Act
        var result = await client.GetShipmentAsync("APX-10000001");

        // Assert
        Assert.Equal(ApiFailureKind.Malformed, result.Failure);
        Assert.True(result.IsTransient);
    }

    [Fact]
    public async Task UploadScansAsync_AnyBatch_PostsCamelCaseJsonToTheShipmentsScansRoute()
    {
        // Arrange
        // A Moq mock of the handler's protected SendAsync: it answers 200 and lets the test verify the request.
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(Respond(HttpStatusCode.OK, """{"accepted":1,"duplicatesSkipped":0,"rejected":0,"rejections":[]}"""));
        var client = ClientFor(handler.Object);

        // Act
        var result = await client.UploadScansAsync("APX-10000001", [ScanItem()]);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value.Accepted);
        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request =>
                request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/v2/shipments/APX-10000001/scans"
                && request.Content!.Headers.ContentType!.MediaType == "application/json"),
            ItExpr.IsAny<CancellationToken>());
    }

    private static ScanUploadItemDto ScanItem() =>
        new("scan::0f8fad5b-d9cb-469f-a165-70867728950e", ScanKind.Pickup, "CR-1001", "Sydney depot",
            new DateTimeOffset(2026, 11, 9, 1, 30, 0, TimeSpan.Zero), null, null);
}
