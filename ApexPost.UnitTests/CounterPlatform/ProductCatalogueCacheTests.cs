using ApexPost.Application.Interfaces;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class ProductCatalogueCacheTests
{
    private const int RequestCount = 1_000;

    [Fact]
    public async Task GetProductsAsync_RepeatedCalls_LoadsRepositoryOnce()
    {
        // Arrange
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

        // Act
        var results = new List<IReadOnlyList<Product>>();
        for (var index = 0; index < RequestCount; index++)
        {
            results.Add(await catalogue.GetProductsAsync());
        }

        // Assert
        // The cache is judged by how many times it reached the repository, not by elapsed time, which would change
        // from one machine and one run to the next.
        Assert.All(results, result => Assert.Equal(products.Count, result.Count));
        Assert.Equal(1, repository.LoadCount);
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
