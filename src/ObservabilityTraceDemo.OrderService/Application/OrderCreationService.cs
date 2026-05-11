using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ObservabilityTraceDemo.OrderService.Observability;

namespace ObservabilityTraceDemo.OrderService.Application;

public sealed class OrderCreationService
{
    private readonly IInventoryClient _inventoryClient;
    private readonly IOrderRepository _repository;
    private readonly ILogger<OrderCreationService> _logger;

    public OrderCreationService(
        IInventoryClient inventoryClient,
        IOrderRepository repository,
        ILogger<OrderCreationService> logger)
    {
        _inventoryClient = inventoryClient;
        _repository = repository;
        _logger = logger;
    }

    public async Task<OrderCreationResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        using var activity = OrderTelemetry.ActivitySource.StartActivity("order.create");
        activity?.SetTag("app.operation", "order.create");
        activity?.SetTag("order.sku", request.Sku);
        activity?.SetTag("order.quantity", request.Quantity);
        activity?.SetTag("order.customer_id", request.CustomerId);

        var startedAt = Stopwatch.GetTimestamp();
        _logger.LogInformation(
            "开始创建订单。Sku={Sku}, Quantity={Quantity}, CustomerId={CustomerId}, TraceId={TraceId}",
            request.Sku,
            request.Quantity,
            request.CustomerId,
            activity?.TraceId.ToString());

        var inventory = await _inventoryClient.CheckAvailabilityAsync(request.Sku, request.Quantity, cancellationToken);
        if (!inventory.Available)
        {
            activity?.SetTag("error.type", "business.insufficient_inventory");
            activity?.SetTag("error.message", "库存不足，无法创建订单。");
            activity?.SetStatus(ActivityStatusCode.Error, "insufficient_inventory");

            // 生产场景里不把 sku 这种潜在高基数字段放进指标标签，避免时间序列爆炸。
            OrderTelemetry.OrdersFailed.Add(1);
            OrderTelemetry.OrderDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

            _logger.LogWarning(
                "库存不足，订单创建失败。Sku={Sku}, RequestedQuantity={RequestedQuantity}, AvailableQuantity={AvailableQuantity}, TraceId={TraceId}",
                request.Sku,
                request.Quantity,
                inventory.AvailableQuantity,
                activity?.TraceId.ToString());

            return new OrderCreationResult(false, Guid.Empty, "insufficient_inventory", inventory.AvailableQuantity);
        }

        var order = new OrderRecord(Guid.NewGuid(), request.Sku, request.Quantity, request.CustomerId, DateTimeOffset.UtcNow);
        activity?.SetTag("order.id", order.OrderId);

        await _repository.SaveAsync(order, cancellationToken);

        OrderTelemetry.OrdersCreated.Add(1);
        OrderTelemetry.OrderDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalSeconds);

        _logger.LogInformation(
            "订单创建成功。OrderId={OrderId}, Sku={Sku}, Quantity={Quantity}, TraceId={TraceId}",
            order.OrderId,
            order.Sku,
            order.Quantity,
            activity?.TraceId.ToString());

        return new OrderCreationResult(true, order.OrderId, null, inventory.AvailableQuantity);
    }
}
