using ApexPost.Tracking.Domain;
using ApexPost.Tracking.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ApexPost.Api.Hosting;

/// <summary>
/// In Development only, brings the tracking schema up to date and adds fictional shipments if there are none, so
/// "Try it out" has data to work with. It never runs in other environments.
/// </summary>
internal sealed class DevelopmentDatabase(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<DevelopmentDatabase> logger) : IHostedService
{
    public const string Setting = "Tracking:PrepareDevelopmentDatabase";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>(Setting))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        if (await db.Shipments.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = clock.GetUtcNow();
        var cities = new[] { "Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide", "Hobart" };
        var branches = new[] { "B001", "B002", "B003" };
        for (var index = 0; index < 12; index++)
        {
            var shipment = Shipment.Create(
                $"APX-{10000001 + index}",
                branches[index % branches.Length],
                new Recipient($"Sample Recipient {index + 1}", $"+61 2 5550 {1000 + index}", $"{index + 1} Example Street", cities[index % cities.Length]),
                0.5m + index,
                now.AddHours(index - 24));

            // Walk the first shipments along the status path so the list has a mix of statuses.
            foreach (var status in new[] { ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery }.Take(index % 4))
            {
                shipment.MoveTo(status, now.AddHours(index - 23));
            }

            db.Shipments.Add(shipment);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Added 12 fictional shipments for development, APX-10000001 to APX-10000012.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
