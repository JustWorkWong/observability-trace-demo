using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.OrderService.Infrastructure;

namespace ObservabilityTraceDemo.OrderService.Tests;

public sealed class OrderDbContextMappingTests
{
    [Fact]
    public void Order_entity_should_map_to_expected_postgresql_table_and_columns()
    {
        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new OrderDbContext(options);
        var entityType = dbContext.Model.FindEntityType(typeof(OrderEntity));

        Assert.NotNull(entityType);
        Assert.Equal("ordering", entityType!.GetSchema());
        Assert.Equal("orders", entityType.GetTableName());
        Assert.Equal("order_id", entityType.FindProperty(nameof(OrderEntity.OrderId))!.GetColumnName());
        Assert.Equal("sku", entityType.FindProperty(nameof(OrderEntity.Sku))!.GetColumnName());
        Assert.Equal("quantity", entityType.FindProperty(nameof(OrderEntity.Quantity))!.GetColumnName());
        Assert.Equal("customer_id", entityType.FindProperty(nameof(OrderEntity.CustomerId))!.GetColumnName());
        Assert.Equal("created_at_utc", entityType.FindProperty(nameof(OrderEntity.CreatedAtUtc))!.GetColumnName());
    }
}
