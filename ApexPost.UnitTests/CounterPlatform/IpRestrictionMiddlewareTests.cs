using System.Net;
using ApexPost.Web.Attributes;
using ApexPost.Web.Configuration;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Http;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class IpRestrictionMiddlewareTests
{
    [Fact]
    public async Task RestrictedEndpoint_RejectsRemoteIpOutsideAllowlist()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IpRestrictedAttribute()),
            "staff"));
        var nextCalled = false;
        var middleware = new IpRestrictionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, new TestOptionsMonitor<SecurityOptions>(new SecurityOptions { AllowedIps = ["127.0.0.1"] }));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task UnmarkedEndpoint_IsNotBlockedByIpAllowlist()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var nextCalled = false;
        var middleware = new IpRestrictionMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, new TestOptionsMonitor<SecurityOptions>(new SecurityOptions { AllowedIps = ["127.0.0.1"] }));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }
}
