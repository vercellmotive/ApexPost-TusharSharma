using System.Text.Json;
using System.Text.Json.Serialization;
using ApexPost.Api.Errors;
using ApexPost.Infrastructure.Persistence;
using ApexPost.Tracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace ApexPost.Api.Hosting;

internal static class ApiServiceRegistration
{
    public static IServiceCollection AddApexPostApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                // Validation errors name fields the way the JSON does: "recipientPhone", not "RecipientPhone".
                options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

                // A number must be sent as a JSON number. The web default also accepts "2.5" as a string, which
                // makes the OpenAPI schema say "number or string" and the generated client weaker.
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            });

        // The OpenAPI generator reads these options rather than the MVC ones, so they must say the same.
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        services.Configure<RouteOptions>(options =>
            options.ConstraintMap.Add(TrackingNumberRouteConstraint.Name, typeof(TrackingNumberRouteConstraint)));

        services.AddApexPostVersioning();
        services.AddApexPostOpenApi();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // The Week 3 context, repositories and services. The shipment service reads branches through them.
        services.AddSqlServerPersistence();
        services.AddShipmentTracking();
        services.AddHostedService<DevelopmentDatabase>();
        return services;
    }
}
