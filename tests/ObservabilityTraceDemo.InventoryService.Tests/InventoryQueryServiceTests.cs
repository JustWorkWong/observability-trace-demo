using Microsoft.Extensions.Logging.Abstractions;
using ObservabilityTraceDemo.InventoryService.Application;

namespace ObservabilityTraceDemo.InventoryService.Tests;

public sealed class InventoryQueryServiceTests
{
    [Fact]
    public async Task GetInventoryAsync_returns_cached_inventory_without_repository_lookup_when_cache_hit()
    {
        var cache = new FakeInventoryCache(new InventorySnapshot("sku-1", 12));
        var repository = new FakeInventoryRepository(new InventorySnapshot("sku-1", 30));
        var service = new InventoryQueryService(cache, repository, NullLogger<InventoryQueryService>.Instance);

        var result = await service.GetInventoryAsync("sku-1", CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.CacheHit);
        Assert.Equal(12, result.AvailableQuantity);
        Assert.Equal(0, repository.GetBySkuCalls);
        Assert.Empty(cache.SetCalls);
    }

    [Fact]
    public async Task GetInventoryAsync_queries_repository_and_populates_cache_when_cache_misses()
    {
        var cache = new FakeInventoryCache(cachedSnapshot: null);
        var repository = new FakeInventoryRepository(new InventorySnapshot("sku-2", 8));
        var service = new InventoryQueryService(cache, repository, NullLogger<InventoryQueryService>.Instance);

        var result = await service.GetInventoryAsync("sku-2", CancellationToken.None);

        Assert.True(result.Found);
        Assert.False(result.CacheHit);
        Assert.Equal(8, result.AvailableQuantity);
        Assert.Equal(1, repository.GetBySkuCalls);
        Assert.Single(cache.SetCalls);
        Assert.Equal("sku-2", cache.SetCalls[0].Sku);
        Assert.Equal(8, cache.SetCalls[0].AvailableQuantity);
    }

    private sealed class FakeInventoryCache : IInventoryCache
    {
        private readonly InventorySnapshot? _cachedSnapshot;

        public FakeInventoryCache(InventorySnapshot? cachedSnapshot)
        {
            _cachedSnapshot = cachedSnapshot;
        }

        public List<InventorySnapshot> SetCalls { get; } = [];

        public Task<InventorySnapshot?> GetAsync(string sku, CancellationToken cancellationToken)
        {
            return Task.FromResult(_cachedSnapshot?.Sku == sku ? _cachedSnapshot : null);
        }

        public Task SetAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            SetCalls.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryRepository : IInventoryRepository
    {
        private readonly InventorySnapshot? _snapshot;

        public FakeInventoryRepository(InventorySnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public int GetBySkuCalls { get; private set; }

        public Task<InventorySnapshot?> GetBySkuAsync(string sku, CancellationToken cancellationToken)
        {
            GetBySkuCalls++;
            return Task.FromResult(_snapshot?.Sku == sku ? _snapshot : null);
        }

        public Task UpsertAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
