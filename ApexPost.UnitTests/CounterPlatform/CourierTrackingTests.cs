using ApexPost.Application.Interfaces;
using ApexPost.Infrastructure.Services;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class CourierTrackingTests
{
    [Fact]
    public async Task LookupExceedingTimeoutThrowsDistinctTimeoutFailure()
    {
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<CourierTrackingTimeoutException>(() =>
            tracking.GetStatusAsync("APX-001"));
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoLookupTimeout()
    {
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tracking.GetStatusAsync("APX-001", cancellation.Token));
    }

    [Fact]
    public async Task CompletedLookupReturnsStatus()
    {
        var tracking = new SimulatedCourierTrackingService(
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);

        var result = await tracking.GetStatusAsync("APX-001");

        Assert.Equal("APX-001", result.TrackingId);
        Assert.Equal("In transit", result.Status);
        Assert.Equal("Zone 3", result.DestinationZone);
    }
}
