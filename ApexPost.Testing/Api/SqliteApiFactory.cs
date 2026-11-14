extern alias api;
using System.Net.Http.Headers;
using ApexPost.Application.Interfaces;
using ApexPost.Testing.Persistence;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.Testing.Api;

/// <summary>
/// The real API pipeline over a real database: controllers, validation, JWT authentication and authorization, the
/// service, <see cref="EfShipmentStore"/> and <see cref="TrackingDbContext"/>, on a private SQLite database in memory.
/// Only the Week 3 branch repository (another bounded context) and the clock are doubles. Authentication is the real
/// JWT bearer handler, configured with a test issuer, audience and a signing key generated for this run.
/// </summary>
public sealed class SqliteApiFactory : WebApplicationFactory<api::Program>
{
    public SqliteTrackingDatabase Database { get; } = new();

    public FakeTimeProvider Clock { get; } = TestTime.NewClock();

    public HttpClient CreateClient(params string[] roles)
    {
        var client = base.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(roles, Make.CourierId));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Tracking:PrepareDevelopmentDatabase", "false");
        ApiFactory.UseTestSecurity(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            // Replace the SQL Server registration of the tracking context with the SQLite test database.
            var sqlServer = services
                .Where(service => service.ServiceType == typeof(TrackingDbContext)
                    || service.ServiceType == typeof(DbContextOptions<TrackingDbContext>)
                    || service.ServiceType == typeof(IDbContextOptionsConfiguration<TrackingDbContext>))
                .ToList();
            foreach (var service in sqlServer)
            {
                services.Remove(service);
            }

            services.AddDbContext<TrackingDbContext>(options => SqliteTrackingDatabase.UseSqlite(options, Database.Connection));
            services.AddSingleton<IBranchRepository, FakeBranches>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Database.Dispose();
        }
    }
}
