using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ObservabilityTraceDemo.InventoryService.Application;
using ObservabilityTraceDemo.InventoryService.Infrastructure;
using ObservabilityTraceDemo.InventoryService.Observability;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddProjectOpenTelemetry(
    activitySources: [InventoryTelemetry.ActivitySourceName],
    meters: [InventoryTelemetry.MeterName],
    includeEntityFramework: true,
    includeRedis: true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("observabilitydb")
        ?? throw new InvalidOperationException("缺少 PostgreSQL 连接串: ConnectionStrings:observabilitydb");

    options.UseNpgsql(connectionString);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("redis")
        ?? throw new InvalidOperationException("缺少 Redis 连接串: ConnectionStrings:redis");

    return ConnectionMultiplexer.Connect(connectionString);
});

builder.Services.AddScoped<IInventoryRepository, EfCoreInventoryRepository>();
builder.Services.AddScoped<IInventoryCache, RedisInventoryCache>();
builder.Services.AddScoped<InventoryQueryService>();
builder.Services.AddScoped<InventorySchemaInitializer>();

var app = builder.Build();

await app.InitializeInventorySchemaAsync();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "InventoryService Swagger";
    options.RoutePrefix = "swagger";
});

app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Ok(new
{
    service = "InventoryService",
    description = "负责库存查询、缓存命中与数据库回源。"
}));

app.MapGet("/api/inventory/{sku}", async (
    string sku,
    InventoryQueryService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetInventoryAsync(sku, cancellationToken);
    return Results.Ok(new InventoryLookupHttpResponse(
        result.Found,
        result.CacheHit,
        result.Sku,
        result.AvailableQuantity,
        Activity.Current?.TraceId.ToString()));
})
.WithTags("库存接口")
.WithSummary("查询库存")
.WithDescription("优先读取 Redis，未命中时回源 PostgreSQL，并在返回前回填缓存。")
.Produces<InventoryLookupHttpResponse>(StatusCodes.Status200OK);

app.MapPost("/api/inventory/seed", async (
    IInventoryRepository repository,
    IInventoryCache cache,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("InventorySeed");
    var seedItems = new[]
    {
        new InventorySnapshot("sku-1", 20),
        new InventorySnapshot("sku-2", 5),
        new InventorySnapshot("sku-3", 0)
    };

    foreach (var item in seedItems)
    {
        await repository.UpsertAsync(item, cancellationToken);
        await cache.RemoveAsync(item.Sku, cancellationToken);
        logger.LogInformation(
            "库存种子写入完成，并已清理对应缓存。Sku={Sku}, AvailableQuantity={AvailableQuantity}, TraceId={TraceId}",
            item.Sku,
            item.AvailableQuantity,
            Activity.Current?.TraceId.ToString());
    }

    return Results.Ok(new
    {
        message = "seeded",
        items = seedItems.Length,
        traceId = Activity.Current?.TraceId.ToString()
    });
})
.WithTags("库存接口")
.WithSummary("写入演示库存种子")
.WithDescription("向库存表写入默认演示数据，并清理对应 Redis 缓存。这样第一次查询会回源 PostgreSQL，第二次查询才命中 Redis。")
.Produces(StatusCodes.Status200OK);

app.Run();

internal static class InventoryServiceStartupExtensions
{
    public static async Task InitializeInventorySchemaAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<InventorySchemaInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
    }
}

public sealed record InventoryLookupHttpResponse(
    bool Found,
    bool CacheHit,
    string Sku,
    int AvailableQuantity,
    string? TraceId);
