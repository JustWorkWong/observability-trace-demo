using System.Net.Http.Json;
using ObservabilityTraceDemo.OrderService.Application;

namespace ObservabilityTraceDemo.OrderService.Infrastructure;

public sealed class InventoryHttpClient(HttpClient httpClient) : IInventoryClient
{
    public async Task<InventoryCheckResult> CheckAvailabilityAsync(string sku, int quantity, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/inventory/{Uri.EscapeDataString(sku)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<InventoryLookupHttpResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("InventoryService 返回了空响应。");

        return new InventoryCheckResult(payload.Found && payload.AvailableQuantity >= quantity, payload.AvailableQuantity);
    }

    private sealed record InventoryLookupHttpResponse(
        bool Found,
        bool CacheHit,
        string Sku,
        int AvailableQuantity,
        string? TraceId);
}
