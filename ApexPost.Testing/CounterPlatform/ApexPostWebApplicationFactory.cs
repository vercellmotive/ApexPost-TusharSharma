extern alias web;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.Testing.CounterPlatform;

// Runs the real pipeline, controllers and services over in-memory persistence, so these tests need no database.
public class ApexPostWebApplicationFactory : WebApplicationFactory<web::Program>
{
    public InMemoryDatabase Database { get; } = InMemoryDatabase.FromSeed();

    /// <summary>
    /// The counter's clock: the middle of a business day, so a sale is never refused or dated by the time the test
    /// happens to run.
    /// </summary>
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Debug);
            logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Debug);
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:SeedOnStartup"] = "false",
                ["Integration:ApiKey"] = CounterTestSettings.IntegrationKey,
                ["Security:AllowedIps:0"] = "127.0.0.1",
                ["Security:AllowedIps:1"] = "::1",
                ["Security:AllowedIps:2"] = "127.0.0.25",
                ["Security:AllowedIps:3"] = "127.0.0.30",
                ["Security:AllowedIps:4"] = "127.0.0.40",
                ["Security:AllowedIps:5"] = "127.0.0.60",
                ["Security:AllowedIps:6"] = "127.0.0.61"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(Database);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IAuditStore, InMemoryAuditStore>();
            services.AddScoped<InMemoryPersistence>();
            services.AddScoped<IUnitOfWork>(Resolve);
            services.AddScoped<IBranchRepository>(Resolve);
            services.AddScoped<IStaffRepository>(Resolve);
            services.AddScoped<IProductRepository>(Resolve);
            services.AddScoped<IStockRepository>(Resolve);
            services.AddScoped<ICounterTransactionRepository>(Resolve);
            services.AddScoped<IShippingZoneRepository>(Resolve);
            services.AddScoped<IStockAdjustmentCommand>(Resolve);
            services.AddScoped<IPriceRevisionCommand>(Resolve);
            services.AddScoped<IDashboardQueries>(Resolve);
            services.AddScoped<IReportQueries>(Resolve);
            services.AddScoped<IScreenQueries>(Resolve);
        });
    }

    private static InMemoryPersistence Resolve(IServiceProvider provider) =>
        provider.GetRequiredService<InMemoryPersistence>();
}
