namespace ObservabilityTraceDemo.OrderService.Application;

public sealed record CreateOrderRequest(string Sku, int Quantity, string CustomerId);

public sealed record InventoryCheckResult(bool Available, int AvailableQuantity);

public sealed record OrderRecord(
    Guid OrderId,
    string Sku,
    int Quantity,
    string CustomerId,
    DateTimeOffset CreatedAtUtc);

public sealed record OrderCreationResult(
    bool Success,
    Guid OrderId,
    string? ErrorCode,
    int AvailableQuantity);

public interface IInventoryClient
{
    Task<InventoryCheckResult> CheckAvailabilityAsync(string sku, int quantity, CancellationToken cancellationToken);
}

public interface IOrderRepository
{
    Task SaveAsync(OrderRecord order, CancellationToken cancellationToken);
}
