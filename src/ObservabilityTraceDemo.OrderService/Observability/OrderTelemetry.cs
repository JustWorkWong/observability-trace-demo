using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ObservabilityTraceDemo.OrderService.Observability;

public static class OrderTelemetry
{
    public const string ActivitySourceName = "order.create";
    public const string MeterName = "ObservabilityTraceDemo.OrderService";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> OrdersCreated = Meter.CreateCounter<long>(
        "orders_created_total",
        unit: "{order}",
        description: "订单创建成功总数。用于看业务吞吐和成功趋势。");

    public static readonly Counter<long> OrdersFailed = Meter.CreateCounter<long>(
        "orders_failed_total",
        unit: "{order}",
        description: "订单创建失败总数。用于看库存不足或其他业务失败趋势。");

    public static readonly Histogram<double> OrderDuration = Meter.CreateHistogram<double>(
        "order_create_duration_ms",
        unit: "ms",
        description: "订单创建耗时。用于观察跨服务调用、数据库写入对下单延迟的影响。");
}
