extern alias web;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

public sealed class ForwardedHeadersPipelineTests
{
    [Fact]
    public async Task TrustedForwardedHttpsIsAppliedBeforeHstsAndRedirectMiddleware()
    {
        using var factory = new ProductionApexPostFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://apexpost.test"),
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
        Assert.False(response.Headers.Contains("Location"));
    }

    private sealed class ProductionApexPostFactory : WebApplicationFactory<web::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ForwardedHeaders:KnownProxies:0"] = "127.0.0.1",
                    ["ForwardedHeaders:KnownProxies:1"] = "::1"
                });
            });
        }
    }
}
