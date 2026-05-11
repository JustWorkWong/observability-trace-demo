using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ObservabilityTraceDemo.InventoryService.Observability;

public static class InventoryTelemetry
{
    public const string ActivitySourceName = "inventory.lookup";
    public const string MeterName = "ObservabilityTraceDemo.InventoryService";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> CacheHitCounter = Meter.CreateCounter<long>(
        "inventory_cache_hit_total",
        unit: "{lookup}",
        description: "库存缓存命中总数。命中越高，说明读取链路越少依赖数据库回源。");

    public static readonly Counter<long> CacheMissCounter = Meter.CreateCounter<long>(
        "inventory_cache_miss_total",
        unit: "{lookup}",
        description: "库存缓存未命中总数。未命中增多通常意味着缓存过期、冷启动或热点漂移。");

    public static readonly Histogram<double> LookupDuration = Meter.CreateHistogram<double>(
        "inventory_lookup_duration_ms",
        unit: "ms",
        description: "库存查询耗时。用于对比缓存命中与数据库回源的延迟差异。");
}
