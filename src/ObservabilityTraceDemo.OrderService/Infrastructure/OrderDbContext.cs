using Microsoft.EntityFrameworkCore;

namespace ObservabilityTraceDemo.OrderService.Infrastructure;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderEntity>(entity =>
        {
            entity.ToTable("orders", schema: "ordering");
            entity.HasKey(order => order.OrderId);
            entity.Property(order => order.OrderId)
                .HasColumnName("order_id");
            entity.Property(order => order.Sku)
                .HasColumnName("sku")
                .HasMaxLength(128);
            entity.Property(order => order.Quantity)
                .HasColumnName("quantity");
            entity.Property(order => order.CustomerId)
                .HasColumnName("customer_id")
                .HasMaxLength(128);
            entity.Property(order => order.CreatedAtUtc)
                .HasColumnName("created_at_utc");
        });
    }
}
