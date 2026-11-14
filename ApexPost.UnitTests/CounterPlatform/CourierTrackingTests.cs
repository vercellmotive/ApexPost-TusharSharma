using ApexPost.Application.Interfaces;
using ApexPost.Infrastructure.Services;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class CourierTrackingTests
{
    [Fact]
    public async Task Lookup_ExceedsTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(100));

        // Act and assert
        await Assert.ThrowsAsync<CourierTrackingTimeoutException>(() =>
            tracking.GetStatusAsync("APX-001"));
    }

    [Fact]
    public async Task Lookup_CallerCancels_ThrowsCancellationNotTimeout()
    {
        // Arrange
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tracking.GetStatusAsync("APX-001", cancellation.Token));
    }

    [Fact]
    public async Task Lookup_CompletesInTime_ReturnsStatus()
    {
        // Arrange
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);

        // Act
        var result = await tracking.GetStatusAsync("APX-001");

        // Assert
        Assert.Equal("APX-001", result.TrackingId);
        Assert.Equal("In transit", result.Status);
        Assert.Equal("Zone 3", result.DestinationZone);
    }
}
