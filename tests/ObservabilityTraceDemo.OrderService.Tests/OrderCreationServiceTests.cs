using Microsoft.Extensions.Logging.Abstractions;
using ObservabilityTraceDemo.OrderService.Application;

namespace ObservabilityTraceDemo.OrderService.Tests;

public sealed class OrderCreationServiceTests
{
    [Fact]
    public async Task CreateOrderAsync_persists_order_when_inventory_is_available()
    {
        var inventoryClient = new FakeInventoryClient(new InventoryCheckResult(true, 10));
        var repository = new FakeOrderRepository();
        var service = new OrderCreationService(inventoryClient, repository, NullLogger<OrderCreationService>.Instance);

        var result = await service.CreateOrderAsync(new CreateOrderRequest("sku-1", 2, "customer-1"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.OrderId);
        Assert.Equal(1, repository.SaveCalls);
        Assert.Single(repository.SavedOrders);
        Assert.Equal("sku-1", repository.SavedOrders[0].Sku);
        Assert.Equal(2, repository.SavedOrders[0].Quantity);
    }

    [Fact]
    public async Task CreateOrderAsync_returns_failure_without_persisting_order_when_inventory_is_insufficient()
    {
        var inventoryClient = new FakeInventoryClient(new InventoryCheckResult(false, 1));
        var repository = new FakeOrderRepository();
        var service = new OrderCreationService(inventoryClient, repository, NullLogger<OrderCreationService>.Instance);

        var result = await service.CreateOrderAsync(new CreateOrderRequest("sku-2", 5, "customer-2"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("insufficient_inventory", result.ErrorCode);
        Assert.Equal(0, repository.SaveCalls);
        Assert.Empty(repository.SavedOrders);
    }

    private sealed class FakeInventoryClient : IInventoryClient
    {
        private readonly InventoryCheckResult _result;

        public FakeInventoryClient(InventoryCheckResult result)
        {
            _result = result;
        }

        public Task<InventoryCheckResult> CheckAvailabilityAsync(string sku, int quantity, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public int SaveCalls { get; private set; }

        public List<OrderRecord> SavedOrders { get; } = [];

        public Task SaveAsync(OrderRecord order, CancellationToken cancellationToken)
        {
            SaveCalls++;
            SavedOrders.Add(order);
            return Task.CompletedTask;
        }
    }
}
