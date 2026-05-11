using System.Text.Json;
using ObservabilityTraceDemo.InventoryService.Application;
using StackExchange.Redis;

namespace ObservabilityTraceDemo.InventoryService.Infrastructure;

public sealed class RedisInventoryCache(IConnectionMultiplexer connectionMultiplexer) : IInventoryCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<InventorySnapshot?> GetAsync(string sku, CancellationToken cancellationToken)
    {
        var payload = await connectionMultiplexer
            .GetDatabase()
            .StringGetAsync(BuildCacheKey(sku));

        if (!payload.HasValue)
        {
            return null;
        }

        return JsonSerializer.Deserialize<InventorySnapshot>(payload.ToString(), SerializerOptions);
    }

    public async Task SetAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(snapshot, SerializerOptions);
        await connectionMultiplexer
            .GetDatabase()
            .StringSetAsync(BuildCacheKey(snapshot.Sku), payload, expiry: TimeSpan.FromMinutes(10));
    }

    public static string BuildCacheKey(string sku)
    {
        return $"inventory:sku:{sku}";
    }
}
