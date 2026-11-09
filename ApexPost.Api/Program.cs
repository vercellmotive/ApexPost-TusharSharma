using ApexPost.Api.Hosting;
using ApexPost.Api.Security;
using Microsoft.AspNetCore.Rewrite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApexPostApi(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Errors without a body, such as 404 for an unknown route or 405, also get ProblemDetails.
app.UseStatusCodePages();

// HTTPS everywhere, and HSTS outside Development. Bearer tokens and API keys must never cross plain HTTP.
app.UseApexPostTransportSecurity();

// Part B clients call /api/shipments without a version. Those URLs keep working as v1, so v1 changes nothing for them.
app.UseRewriter(new RewriteOptions().AddRewrite("^api/shipments(.*)$", "api/v1/shipments$1", skipRemainingRules: true));

if (app.Environment.IsDevelopment())
{
    // The documents and Swagger UI exist only on a developer's machine. Hiding them elsewhere is not security:
    // every endpoint is protected the same way whether or not it is documented.
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        // The Authorize button keeps the token in the browser's local storage (Development only), so it survives a refresh
        // and every "Try it out" sends it.
        options.EnablePersistAuthorization();
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ApexPost Logistics API";
        foreach (var (documentName, label, _) in ApiVersions.Documents)
        {
            options.SwaggerEndpoint($"/openapi/{documentName}.json", label);
        }
    });
}

// Before authentication, so a flood of bad tokens or keys is turned away cheaply.
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapPartnerEndpoints();
app.MapDevelopmentTokens();

app.Run();

/// <summary>The API entry point. Public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
