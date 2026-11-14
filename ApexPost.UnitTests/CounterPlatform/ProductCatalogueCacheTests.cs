using System.Diagnostics;
using ApexPost.Application.Interfaces;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit.Abstractions;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class ProductCatalogueCacheTests
{
    private const int RequestCount = 10_000;
    private readonly ITestOutputHelper _output;

    public ProductCatalogueCacheTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task CatalogueCacheAvoidsRepeatedRepositoryLoads()
    {
        var products = Enumerable.Range(1, 30)
            .Select(index => new Product
            {
                Id = new Sku($"SKU-{index:D3}"),
                Name = $"Product {index}",
                Category = "Stationery",
                UnitPrice = new Money(index),
                TaxCategory = TaxCategory.StandardGst
            })
            .ToList();
        var repository = new CountingProductRepository(products);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var catalogue = new ProductCatalogueService(repository, cache);

        var cachedTimer = Stopwatch.StartNew();
        for (var index = 0; index < RequestCount; index++)
        {
            var result = await catalogue.GetProductsAsync();
            Assert.Equal(products.Count, result.Count);
        }

        cachedTimer.Stop();
        var cachedLoads = repository.LoadCount;

        var uncachedTimer = Stopwatch.StartNew();
        for (var index = 0; index < RequestCount; index++)
        {
            var result = await repository.GetAllAsync();
            Assert.Equal(products.Count, result.Count);
        }

        uncachedTimer.Stop();
        var uncachedLoads = repository.LoadCount - cachedLoads;

        Assert.Equal(1, cachedLoads);
        Assert.Equal(RequestCount, uncachedLoads);
        _output.WriteLine(
            "Catalogue measurement: requests={0}, productCount={1}, cachedBackingLoads={2}, " +
            "uncachedBackingLoads={3}, cachedElapsedMs={4:F2}, uncachedElapsedMs={5:F2}. " +
            "Elapsed values are diagnostic; backing-load counts are the deterministic assertion.",
            RequestCount,
            products.Count,
            cachedLoads,
            uncachedLoads,
            cachedTimer.Elapsed.TotalMilliseconds,
            uncachedTimer.Elapsed.TotalMilliseconds);
    }

    private sealed class CountingProductRepository(IReadOnlyList<Product> products) : IProductRepository
    {
        private int _loadCount;

        public int LoadCount => Volatile.Read(ref _loadCount);

        public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _loadCount);
            return Task.FromResult(products);
        }

        public Task<Product?> GetBySkuAsync(ApexPost.Domain.ValueObjects.Sku sku, CancellationToken ct = default) =>
            Task.FromResult(products.FirstOrDefault(product => product.Id == sku));

        public void Add(Product product)
        {
        }

        public void Update(Product product)
        {
        }
    }
}
