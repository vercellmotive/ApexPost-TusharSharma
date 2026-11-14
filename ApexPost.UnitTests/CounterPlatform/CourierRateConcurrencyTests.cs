using ApexPost.Application.Interfaces;
using ApexPost.Application.Pricing;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class CourierRateConcurrencyTests
{
    private static readonly ShippingDetails Parcel =
        new(true, Weight.FromKilograms(1m), new Dimensions(1, 1, 1), "Zone 1", ServiceLevel.Standard);

    [Fact]
    public async Task RateLookups_NeverExceedTheConfiguredConcurrencyLimit()
    {
        var zones = new TrackingZoneLookup();
        var service = new SimulatedCourierRateService(new TieredShippingPricingStrategy(), zones, new CourierRateGate(3), TimeSpan.Zero);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.GetEstimatedCourierRateAsync(Parcel)));

        Assert.InRange(zones.MaxInFlight, 2, 3);
    }

    [Fact]
    public async Task ZoneLookup_LoadsTheRepositoryOnceAndMatchesAnyCase()
    {
        var repository = new CountingZoneRepository();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var lookup = new CachedShippingZoneLookup(repository, cache);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => lookup.FindByNameAsync("ZONE 1").AsTask()));

        Assert.Equal(1, repository.LoadCount);
        Assert.Equal(1.5m, (await lookup.FindByNameAsync("zone 1"))!.Multiplier);
        Assert.Null(await lookup.FindByNameAsync("Zone 99"));
    }

    private sealed class TrackingZoneLookup : IShippingZoneLookup
    {
        private int _inFlight;
        private int _maxInFlight;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);

        public async ValueTask<ShippingZone?> FindByNameAsync(string name, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref _inFlight);
            InterlockedMax(current);
            await Task.Delay(10, ct);
            Interlocked.Decrement(ref _inFlight);
            return new ShippingZone { Id = name, Name = name, Multiplier = 1m };
        }

        private void InterlockedMax(int value)
        {
            int snapshot;
            do
            {
                snapshot = Volatile.Read(ref _maxInFlight);
            }
            while (value > snapshot && Interlocked.CompareExchange(ref _maxInFlight, value, snapshot) != snapshot);
        }
    }

    private sealed class CountingZoneRepository : IShippingZoneRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public async Task<IReadOnlyList<ShippingZone>> GetAllAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _loadCount);
            await Task.Delay(5, ct);
            return [new ShippingZone { Id = "Zone 1", Name = "Zone 1", Multiplier = 1.5m }];
        }
    }
}
