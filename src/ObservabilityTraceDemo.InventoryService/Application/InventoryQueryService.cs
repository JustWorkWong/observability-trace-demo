using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ObservabilityTraceDemo.InventoryService.Infrastructure;
using ObservabilityTraceDemo.InventoryService.Observability;

namespace ObservabilityTraceDemo.InventoryService.Application;

public sealed class InventoryQueryService
{
    private readonly IInventoryCache _cache;
    private readonly IInventoryRepository _repository;
    private readonly ILogger<InventoryQueryService> _logger;

    public InventoryQueryService(
        IInventoryCache cache,
        IInventoryRepository repository,
        ILogger<InventoryQueryService> logger)
    {
        _cache = cache;
        _repository = repository;
        _logger = logger;
    }

    public async Task<InventoryLookupResult> GetInventoryAsync(string sku, CancellationToken cancellationToken)
    {
        using var activity = InventoryTelemetry.ActivitySource.StartActivity("inventory.lookup");
        activity?.SetTag("app.operation", "inventory.lookup");
        activity?.SetTag("inventory.sku", sku);
        activity?.SetTag("db.schema", "inventory");

        var startedAt = Stopwatch.GetTimestamp();
        InventorySnapshot? cachedSnapshot;

        using (var cacheReadActivity = InventoryTelemetry.ActivitySource.StartActivity("inventory.cache.read"))
        {
            cacheReadActivity?.SetTag("db.system", "redis");
            cacheReadActivity?.SetTag("db.operation", "GET");
            cacheReadActivity?.SetTag("cache.key", RedisInventoryCache.BuildCacheKey(sku));
            cachedSnapshot = await _cache.GetAsync(sku, cancellationToken);
        }

        if (cachedSnapshot is not null)
        {
            activity?.SetTag("cache.hit", true);
            InventoryTelemetry.CacheHitCounter.Add(1);
            InventoryTelemetry.LookupDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

            _logger.LogInformation(
                "库存缓存命中。Sku={Sku}, AvailableQuantity={AvailableQuantity}, TraceId={TraceId}",
                sku,
                cachedSnapshot.AvailableQuantity,
                activity?.TraceId.ToString());

            return new InventoryLookupResult(true, true, sku, cachedSnapshot.AvailableQuantity);
        }

        activity?.SetTag("cache.hit", false);
        InventoryTelemetry.CacheMissCounter.Add(1);

        _logger.LogInformation(
            "库存缓存未命中，准备回源数据库。Sku={Sku}, TraceId={TraceId}",
            sku,
            activity?.TraceId.ToString());

        var persistedSnapshot = await _repository.GetBySkuAsync(sku, cancellationToken);
        if (persistedSnapshot is null)
        {
            activity?.SetTag("error.type", "business.inventory_not_found");
            activity?.SetStatus(ActivityStatusCode.Error, "inventory_not_found");
            InventoryTelemetry.LookupDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

            _logger.LogWarning(
                "库存不存在。Sku={Sku}, TraceId={TraceId}",
                sku,
                activity?.TraceId.ToString());

            return new InventoryLookupResult(false, false, sku, 0);
        }

        using (var cachePopulateActivity = InventoryTelemetry.ActivitySource.StartActivity("inventory.cache.populate"))
        {
            cachePopulateActivity?.SetTag("db.system", "redis");
            cachePopulateActivity?.SetTag("db.operation", "SET");
            cachePopulateActivity?.SetTag("cache.key", RedisInventoryCache.BuildCacheKey(sku));
            await _cache.SetAsync(persistedSnapshot, cancellationToken);
        }

        InventoryTelemetry.LookupDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        _logger.LogInformation(
            "库存数据库回源完成，并已回填缓存。Sku={Sku}, AvailableQuantity={AvailableQuantity}, TraceId={TraceId}",
            sku,
            persistedSnapshot.AvailableQuantity,
            activity?.TraceId.ToString());

        return new InventoryLookupResult(true, false, sku, persistedSnapshot.AvailableQuantity);
    }
}
