using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Api.Tests;

/// <summary>The real API pipeline with the store, the Week 3 branches and the clock replaced by in-memory doubles.</summary>
public sealed class ApiFactory(string environment = "Testing") : WebApplicationFactory<Program>
{
    public InMemoryShipmentStore Store { get; } = new();
    public TestClock Clock { get; } = new();

    /// <summary>When set, every store call throws this, to exercise the global exception handler.</summary>
    public Exception? StoreFailure { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Tracking:PrepareDevelopmentDatabase", "false");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IShipmentStore>(_ => StoreFailure is null ? Store : new FailingStore(StoreFailure));
            services.AddSingleton<IBranchRepository, FakeBranches>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    private sealed class FailingStore(Exception failure) : IShipmentStore
    {
        public Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default) => throw failure;
        public Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<Tracking.Domain.Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public void Add(Tracking.Domain.Shipment shipment) => throw failure;
        public void Remove(Tracking.Domain.Shipment shipment) => throw failure;
        public Task SaveChangesAsync(CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyList<Tracking.Domain.TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<Tracking.Domain.TrackingScan> scans, CancellationToken ct = default) => throw failure;
    }
}
