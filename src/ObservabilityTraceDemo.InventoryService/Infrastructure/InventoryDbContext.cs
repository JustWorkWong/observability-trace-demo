using Microsoft.EntityFrameworkCore;

namespace ObservabilityTraceDemo.InventoryService.Infrastructure;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItemEntity> StockItems => Set<InventoryItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItemEntity>(entity =>
        {
            entity.ToTable("stock_items", schema: "inventory");
            entity.HasKey(item => item.Sku);
            entity.Property(item => item.Sku)
                .HasColumnName("sku")
                .HasMaxLength(128);
            entity.Property(item => item.AvailableQuantity)
                .HasColumnName("available_quantity");
            entity.Property(item => item.UpdatedAtUtc)
                .HasColumnName("updated_at_utc");
        });
    }
}
