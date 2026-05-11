namespace ObservabilityTraceDemo.OrderService.Infrastructure;

public sealed class OrderEntity
{
    public Guid OrderId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
