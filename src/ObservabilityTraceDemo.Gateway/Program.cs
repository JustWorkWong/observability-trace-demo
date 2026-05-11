using System.Diagnostics;
using ObservabilityTraceDemo.Gateway.Observability;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddProjectOpenTelemetry(
    activitySources: [GatewayTelemetry.ActivitySourceName],
    meters: [GatewayTelemetry.MeterName]);

builder.Services.AddProblemDetails();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    // YARP 默认只把 Address 当作普通地址字符串处理。
    // 加上服务发现解析器后，Gateway 才会解析 https+http://orderservice 这种地址。
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "Gateway",
    description = "负责把 /api/orders/* 和 /api/inventory/* 转发到对应业务服务。"
}));

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    using var activity = GatewayTelemetry.ActivitySource.StartActivity("gateway.route");
    activity?.SetTag("app.operation", "gateway.route");
    activity?.SetTag("http.request_path", context.Request.Path.Value);
    activity?.SetTag("http.request_method", context.Request.Method);

    var startedAt = Stopwatch.GetTimestamp();
    await next();

    var durationSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
    var routeGroup = ResolveRouteGroup(context.Request.Path);
    activity?.SetTag("gateway.route_group", routeGroup);
    activity?.SetTag("http.response.status_code", context.Response.StatusCode);

    GatewayTelemetry.RequestCount.Add(
        1,
        new KeyValuePair<string, object?>("gateway.route_group", routeGroup),
        new KeyValuePair<string, object?>("http.status_code", context.Response.StatusCode));

    GatewayTelemetry.RequestDuration.Record(
        durationSeconds,
        new KeyValuePair<string, object?>("gateway.route_group", routeGroup),
        new KeyValuePair<string, object?>("http.status_code", context.Response.StatusCode));

    if (context.Response.StatusCode >= 500)
    {
        activity?.SetStatus(ActivityStatusCode.Error, "gateway_upstream_error");
        GatewayTelemetry.UpstreamFailureCount.Add(
            1,
            new KeyValuePair<string, object?>("gateway.route_group", routeGroup));
    }

    app.Logger.LogInformation(
        "网关转发完成。Method={Method}, Path={Path}, RouteGroup={RouteGroup}, StatusCode={StatusCode}, DurationSeconds={DurationSeconds}, TraceId={TraceId}",
        context.Request.Method,
        context.Request.Path.Value,
        routeGroup,
        context.Response.StatusCode,
        durationSeconds,
        activity?.TraceId.ToString());
});

app.MapReverseProxy();
app.Run();

static string ResolveRouteGroup(PathString path)
{
    if (path.StartsWithSegments("/api/orders"))
    {
        return "orders";
    }

    if (path.StartsWithSegments("/api/inventory"))
    {
        return "inventory";
    }

    return "unknown";
}
