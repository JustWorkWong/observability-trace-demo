using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.InventoryService.Infrastructure;

namespace ObservabilityTraceDemo.InventoryService.Tests;

public sealed class InventoryDbContextMappingTests
{
    [Fact]
    public void Inventory_entity_should_map_to_expected_postgresql_table_and_columns()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new InventoryDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(InventoryItemEntity));

        Assert.NotNull(entityType);
        Assert.Equal("inventory", entityType!.GetSchema());
        Assert.Equal("stock_items", entityType.GetTableName());
        Assert.Equal("sku", entityType.FindProperty(nameof(InventoryItemEntity.Sku))!.GetColumnName());
        Assert.Equal("available_quantity", entityType.FindProperty(nameof(InventoryItemEntity.AvailableQuantity))!.GetColumnName());
        Assert.Equal("updated_at_utc", entityType.FindProperty(nameof(InventoryItemEntity.UpdatedAtUtc))!.GetColumnName());
    }
}
