var builder = DistributedApplication.CreateBuilder(args);

/*----------------------------------------------------------------------------
 * AppHost 负责“本地拓扑编排”：
 * 1. 用容器拉起 PostgreSQL 与 Redis。
 * 2. 用本地进程跑 Gateway / OrderService / InventoryService。
 * 3. 统一把 OTLP endpoint 下发给所有业务服务。
 *
 * 观测栈本身单独放在 infra/observability/docker-compose.yml。
 * 这样职责更清楚：AppHost 管业务拓扑，Compose 管观测基础设施。
 *--------------------------------------------------------------------------*/
var postgresPassword = builder.AddParameter("postgres-password", value: "postgres", secret: false);
var postgres = builder.AddPostgres("postgres", postgresPassword);
var observabilityDatabase = postgres.AddDatabase("observabilitydb");

var redis = builder.AddRedis("redis");
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"] ?? "http://localhost:14318";

var inventoryService = builder.AddProject<Projects.ObservabilityTraceDemo_InventoryService>("inventoryservice")
    .WithReference(observabilityDatabase)
    .WithReference(redis)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observabilityDatabase)
    .WaitFor(redis);

var orderService = builder.AddProject<Projects.ObservabilityTraceDemo_OrderService>("orderservice")
    .WithReference(observabilityDatabase)
    // OrderService 需要通过服务发现调用 InventoryService。
    // 如果不显式引用，订单服务运行时拿不到 inventoryservice 的发现信息，
    // HttpClient 会把它当成普通主机名解析，最终退化成 inventoryservice:443 并报“主机不存在”。
    .WithReference(inventoryService)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(observabilityDatabase)
    .WaitFor(inventoryService);

builder.AddProject<Projects.ObservabilityTraceDemo_Gateway>("gateway")
    .WithReference(orderService)
    .WithReference(inventoryService)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WaitFor(orderService)
    .WaitFor(inventoryService);

builder.Build().Run();
