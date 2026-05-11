using Microsoft.EntityFrameworkCore;

namespace ObservabilityTraceDemo.InventoryService.Infrastructure;

public sealed class InventorySchemaInitializer(InventoryDbContext dbContext, ILogger<InventorySchemaInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS inventory;

            CREATE TABLE IF NOT EXISTS inventory.stock_items
            (
                sku varchar(128) PRIMARY KEY,
                available_quantity integer NOT NULL,
                updated_at_utc timestamptz NOT NULL
            );
            """,
            cancellationToken);

        logger.LogInformation("库存 schema 初始化完成。Schema={Schema}, Table={Table}", "inventory", "stock_items");
    }
}
