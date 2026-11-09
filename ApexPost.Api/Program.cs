using ApexPost.Api.Hosting;
using Microsoft.AspNetCore.Rewrite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApexPostApi();

var app = builder.Build();

app.UseExceptionHandler();

// Errors without a body, such as 404 for an unknown route or 405, also get ProblemDetails.
app.UseStatusCodePages();
app.UseHttpsRedirection();

// Part B clients call /api/shipments without a version. Those URLs keep working as v1, so v1 changes nothing for them.
app.UseRewriter(new RewriteOptions().AddRewrite("^api/shipments(.*)$", "api/v1/shipments$1", skipRemainingRules: true));

if (app.Environment.IsDevelopment())
{
    // The documents and Swagger UI exist only on a developer's machine. Hiding them elsewhere is not security:
    // every endpoint is protected the same way whether or not it is documented.
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "ApexPost Logistics API";
        foreach (var (documentName, label, _) in ApiVersions.Documents)
        {
            options.SwaggerEndpoint($"/openapi/{documentName}.json", label);
        }
    });
}

app.MapControllers();

app.Run();

/// <summary>The API entry point. Public so integration tests can host it with WebApplicationFactory.</summary>
public partial class Program;
