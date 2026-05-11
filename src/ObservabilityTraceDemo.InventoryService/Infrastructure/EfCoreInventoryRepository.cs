using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.InventoryService.Application;

namespace ObservabilityTraceDemo.InventoryService.Infrastructure;

public sealed class EfCoreInventoryRepository(InventoryDbContext dbContext) : IInventoryRepository
{
    public async Task<InventorySnapshot?> GetBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        var entity = await dbContext.StockItems
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Sku == sku, cancellationToken);

        return entity is null ? null : new InventorySnapshot(entity.Sku, entity.AvailableQuantity);
    }

    public async Task UpsertAsync(InventorySnapshot snapshot, CancellationToken cancellationToken)
    {
        var entity = await dbContext.StockItems.SingleOrDefaultAsync(item => item.Sku == snapshot.Sku, cancellationToken);
        if (entity is null)
        {
            dbContext.StockItems.Add(new InventoryItemEntity
            {
                Sku = snapshot.Sku,
                AvailableQuantity = snapshot.AvailableQuantity,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            entity.AvailableQuantity = snapshot.AvailableQuantity;
            entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
