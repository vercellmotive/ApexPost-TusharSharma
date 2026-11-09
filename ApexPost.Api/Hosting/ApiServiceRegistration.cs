using System.Text.Json;
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
            .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        services.Configure<RouteOptions>(options =>
            options.ConstraintMap.Add(TrackingNumberRouteConstraint.Name, typeof(TrackingNumberRouteConstraint)));

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // The Week 3 context, repositories and services. The shipment service reads branches through them.
        services.AddSqlServerPersistence();
        services.AddShipmentTracking();
        services.AddHostedService<DevelopmentDatabase>();
        return services;
    }
}
