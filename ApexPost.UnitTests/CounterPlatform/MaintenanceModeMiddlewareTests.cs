using ApexPost.Web.Configuration;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Http;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class MaintenanceModeMiddlewareTests
{
    [Fact]
    public async Task ApplicationPage_DuringMaintenance_Returns503WithRetryAfter()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/Catalog";
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("120", context.Response.Headers.RetryAfter);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task HealthEndpoint_DuringMaintenance_RemainsAvailable()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";
        var middleware = CreateMiddleware(_ =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static MaintenanceModeMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, new TestOptionsMonitor<MaintenanceOptions>(new MaintenanceOptions { Enabled = true }));
}
