using System.Diagnostics;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.OrderService.Application;
using ObservabilityTraceDemo.OrderService.Infrastructure;
using ObservabilityTraceDemo.OrderService.Observability;

var builder = WebApplication.CreateBuilder(args);

/*----------------------------------------------------------------------------
 * 这里先挂通用底座，再补项目专属观测：
 * 1. AddServiceDefaults 负责通用日志、Trace、Metric、健康检查和 OTLP 导出。
 * 2. AddProjectOpenTelemetry 负责把订单服务自己的 ActivitySource / Meter 注册进去。
 * 3. includeEntityFramework=true 会自动把 EF Core 的数据库访问 span 接进 Trace。
 *--------------------------------------------------------------------------*/
builder.AddServiceDefaults();
builder.AddProjectOpenTelemetry(
    activitySources: [OrderTelemetry.ActivitySourceName],
    meters: [OrderTelemetry.MeterName],
    includeEntityFramework: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

builder.Services.AddHttpClient<IInventoryClient, InventoryHttpClient>(client =>
{
    // 默认走 Aspire 服务发现；如果你想脱离 AppHost 单独调试，也可以用配置覆盖成固定 URL。
    var inventoryBaseAddress = builder.Configuration["DownstreamServices:InventoryServiceBaseAddress"]
        ?? "https+http://inventoryservice";
    client.BaseAddress = new Uri(inventoryBaseAddress);
});

builder.Services.AddDbContext<OrderDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("observabilitydb")
        ?? throw new InvalidOperationException("缺少 PostgreSQL 连接串: ConnectionStrings:observabilitydb");

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IOrderRepository, EfCoreOrderRepository>();
builder.Services.AddScoped<OrderCreationService>();
builder.Services.AddScoped<OrderSchemaInitializer>();

var app = builder.Build();

await app.InitializeOrderSchemaAsync();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "OrderService Swagger";
    options.RoutePrefix = "swagger";
});

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new
{
    service = "OrderService",
    description = "负责创建订单，并调用 InventoryService 做库存校验。"
}));

app.MapPost("/api/orders", async (
    CreateOrderHttpRequest request,
    OrderCreationService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.CreateOrderAsync(
        new CreateOrderRequest(request.Sku, request.Quantity, request.CustomerId),
        cancellationToken);

    if (!result.Success)
    {
        return Results.BadRequest(new CreateOrderHttpResponse(
            result.OrderId,
            request.Sku,
            request.Quantity,
            request.CustomerId,
            result.AvailableQuantity,
            result.ErrorCode,
            Activity.Current?.TraceId.ToString()));
    }

    return Results.Created(
        $"/api/orders/{result.OrderId}",
        new CreateOrderHttpResponse(
            result.OrderId,
            request.Sku,
            request.Quantity,
            request.CustomerId,
            result.AvailableQuantity,
            null,
            Activity.Current?.TraceId.ToString()));
})
.WithTags("订单接口")
.WithSummary("创建订单")
.WithDescription("调用库存服务校验库存，成功后写入订单表，并返回订单结果。")
.Produces<CreateOrderHttpResponse>(StatusCodes.Status201Created)
.Produces<CreateOrderHttpResponse>(StatusCodes.Status400BadRequest);

app.Run();

static partial class OrderServiceApplication
{
}

internal static class OrderServiceStartupExtensions
{
    public static async Task InitializeOrderSchemaAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<OrderSchemaInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
    }
}

public sealed class CreateOrderHttpRequest
{
    [DefaultValue("sku-1")]
    public string Sku { get; init; } = "sku-1";

    [DefaultValue(2)]
    public int Quantity { get; init; } = 2;

    [DefaultValue("customer-1")]
    public string CustomerId { get; init; } = "customer-1";
}

public sealed record CreateOrderHttpResponse(
    Guid OrderId,
    string Sku,
    int Quantity,
    string CustomerId,
    int AvailableQuantity,
    string? ErrorCode,
    string? TraceId);
