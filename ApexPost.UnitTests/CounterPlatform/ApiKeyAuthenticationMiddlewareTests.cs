using ApexPost.Web.Attributes;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Http;
using ApexPost.Web.Configuration;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class ApiKeyAuthenticationMiddlewareTests
{
    [Fact]
    public async Task ProtectedEndpointReturnsServerErrorWhenKeyIsNotConfigured()
    {
        var context = CreateProtectedContext();
        var middleware = CreateMiddleware(new IntegrationOptions(), _ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointRejectsMissingOrInvalidKey()
    {
        var configuration = CreateConfiguration();
        var missing = CreateProtectedContext();
        await CreateMiddleware(configuration, _ => Task.CompletedTask).InvokeAsync(missing);
        Assert.Equal(StatusCodes.Status401Unauthorized, missing.Response.StatusCode);

        var invalid = CreateProtectedContext();
        invalid.Request.Headers["X-Api-Key"] = "incorrect";
        await CreateMiddleware(configuration, _ => Task.CompletedTask).InvokeAsync(invalid);
        Assert.Equal(StatusCodes.Status401Unauthorized, invalid.Response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointAcceptsConfiguredKey()
    {
        var context = CreateProtectedContext();
        context.Request.Headers["X-Api-Key"] = "test-key";
        var nextCalled = false;
        var middleware = CreateMiddleware(CreateConfiguration(), _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public void ApiKeysResolveIndependentPartnerIdentities()
    {
        var validator = new PartnerApiKeyValidator(new TestOptionsMonitor<IntegrationOptions>(new IntegrationOptions
        {
            Partners = new Dictionary<string, PartnerCredentialOptions>
            {
                ["CourierA"] = new() { ApiKey = "courier-a-key" },
                ["CourierB"] = new() { ApiKey = "courier-b-key" }
            }
        }));

        Assert.Equal("CourierA", validator.FindPartnerId("courier-a-key"));
        Assert.Equal("CourierB", validator.FindPartnerId("courier-b-key"));
        Assert.Null(validator.FindPartnerId("invalid-key"));
    }

    private static DefaultHttpContext CreateProtectedContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ApiKeyRequiredAttribute()),
            "integration"));
        return context;
    }

    private static IntegrationOptions CreateConfiguration() => new() { ApiKey = "test-key" };

    private static ApiKeyAuthenticationMiddleware CreateMiddleware(IntegrationOptions options, RequestDelegate next) =>
        new(next, new PartnerApiKeyValidator(new TestOptionsMonitor<IntegrationOptions>(options)));
}
