namespace ObservabilityTraceDemo.InventoryService.Application;

public sealed record InventorySnapshot(string Sku, int AvailableQuantity);

public sealed record InventoryLookupResult(
    bool Found,
    bool CacheHit,
    string Sku,
    int AvailableQuantity);

public interface IInventoryCache
{
    Task<InventorySnapshot?> GetAsync(string sku, CancellationToken cancellationToken);

    Task SetAsync(InventorySnapshot snapshot, CancellationToken cancellationToken);

    Task RemoveAsync(string sku, CancellationToken cancellationToken);
}

public interface IInventoryRepository
{
    Task<InventorySnapshot?> GetBySkuAsync(string sku, CancellationToken cancellationToken);

    Task UpsertAsync(InventorySnapshot snapshot, CancellationToken cancellationToken);
}
