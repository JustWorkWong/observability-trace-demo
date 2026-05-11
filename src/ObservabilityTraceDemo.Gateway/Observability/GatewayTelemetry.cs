using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ObservabilityTraceDemo.Gateway.Observability;

public static class GatewayTelemetry
{
    public const string ActivitySourceName = "gateway.route";
    public const string MeterName = "ObservabilityTraceDemo.Gateway";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> RequestCount = Meter.CreateCounter<long>(
        "gateway_requests_total",
        unit: "{request}",
        description: "网关完成转发的请求总数。用于观察入口流量和状态码分布。");

    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "gateway_request_duration_seconds",
        unit: "s",
        description: "网关请求耗时。使用秒作为标准时间单位，便于与 Prometheus 生态中的其他耗时指标统一分析。");

    public static readonly Counter<long> UpstreamFailureCount = Meter.CreateCounter<long>(
        "gateway_upstream_failures_total",
        unit: "{request}",
        description: "网关观察到的上游失败总数。用于快速判断错误是否集中在下游依赖。");
}
