using ApexPost.Infrastructure.Persistence;
using ApexPost.Tracking.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Tracking.Infrastructure;

public static class TrackingRegistration
{
    /// <summary>
    /// Registers the tracking context, store and service. The context reads the same <c>ApexPostDb</c> connection
    /// string as the Week 3 context, so both work against one database.
    /// </summary>
    public static IServiceCollection AddShipmentTracking(this IServiceCollection services)
    {
        services.AddDbContext<TrackingDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString(SqlServerRegistration.ConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"Connection string '{SqlServerRegistration.ConnectionStringName}' is not configured. Set it with dotnet user-secrets; see README.md.");
            options.UseSqlServer(connectionString, Configure);
        });

        services.AddScoped<IShipmentStore, EfShipmentStore>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    internal static void Configure(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable(TrackingDbContext.MigrationsHistoryTable, TrackingDbContext.Schema)
            .EnableRetryOnFailure(3);
}

// dotnet-ef needs a context at design time. Like the Week 3 factory, it holds no credentials and only generates code.
public sealed class TrackingDesignTimeFactory : IDesignTimeDbContextFactory<TrackingDbContext>
{
    public TrackingDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("APEXPOST_DESIGN_CONNECTION")
            ?? "Server=localhost;Database=ApexPostDb;Integrated Security=true;TrustServerCertificate=true";
        return new TrackingDbContext(new DbContextOptionsBuilder<TrackingDbContext>()
            .UseSqlServer(connectionString, TrackingRegistration.Configure)
            .Options);
    }
}
