using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.OrderService.Application;

namespace ObservabilityTraceDemo.OrderService.Infrastructure;

public sealed class EfCoreOrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    public async Task SaveAsync(OrderRecord order, CancellationToken cancellationToken)
    {
        dbContext.Orders.Add(new OrderEntity
        {
            OrderId = order.OrderId,
            Sku = order.Sku,
            Quantity = order.Quantity,
            CustomerId = order.CustomerId,
            CreatedAtUtc = order.CreatedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
