namespace ObservabilityTraceDemo.InventoryService.Infrastructure;

public sealed class InventoryItemEntity
{
    public string Sku { get; set; } = string.Empty;

    public int AvailableQuantity { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
