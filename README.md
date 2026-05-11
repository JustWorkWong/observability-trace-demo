# Observability Trace Demo

这是一个面向教学与演示的本地分布式可观测性仓库。

目标不是只把服务跑起来，而是把下面这条链路完整看清楚：

`Client -> Gateway -> OrderService -> InventoryService -> Redis -> PostgreSQL`

你可以在这套仓库里同时看到：

- `Trace`：一次请求经过了哪些服务、缓存、数据库
- `Metric`：吞吐、错误率、P95/P99、缓存命中率、Collector 接收/导出量
- `Log`：结构化业务日志，以及如何通过 `TraceId` 反查 Trace

## 1. 仓库组成

- `src/ObservabilityTraceDemo.AppHost`
  - 编排业务服务、PostgreSQL、Redis
- `src/ObservabilityTraceDemo.Gateway`
  - YARP 网关入口
- `src/ObservabilityTraceDemo.OrderService`
  - 下单与订单落库
- `src/ObservabilityTraceDemo.InventoryService`
  - 缓存读取、数据库回源、库存种子
- `infra/observability`
  - Collector + Prometheus + Loki + Tempo + Grafana

## 2. 启动顺序

### 第一步：启动观测基础设施

在仓库根目录执行：

```powershell
cd E:\wfcodes\observability-trace-demo\infra\observability
docker compose up -d
```

### 第二步：启动业务拓扑

新开一个终端，在仓库根目录执行：

```powershell
cd E:\wfcodes\observability-trace-demo
dotnet run --project .\src\ObservabilityTraceDemo.AppHost\
```

AppHost 启动后会拉起：

- Gateway
- OrderService
- InventoryService
- PostgreSQL
- Redis

## 3. 访问入口

- Grafana: [http://localhost:33000](http://localhost:33000)
- Prometheus: [http://localhost:9090](http://localhost:9090)
- OTel Collector gRPC: `http://localhost:14318`
- OTel Collector HTTP: 预留为 `14319`，当前 Compose 默认不对宿主机暴露

说明：

- 当前版本里，`Loki` 和 `Tempo` 主要通过容器内部网络给 `Grafana` 使用，默认不对宿主机暴露 HTTP 入口。
- 你日常查看日志和链路时，直接从 `Grafana -> Explore` 进入即可。

Grafana 默认账号：

```text
admin / admin
```

## 4. 演示步骤

### 4.1 写入库存种子

先初始化库存：

```powershell
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:<gateway-port>/api/inventory/seed'
```

这里的 `<gateway-port>` 不是固定值。

因为 `AppHost` 会在本机动态分配端口，所以请从下面任一位置确认网关地址：

1. `AppHost` 控制台输出
2. `Aspire Dashboard`
3. `Gateway` 启动日志里的 `Now listening on: http://localhost:xxxxx`

这个接口会把演示库存写入 PostgreSQL，并清理对应 Redis 缓存。
这样下一步第一次查 `sku-1` 时会先缓存未命中，再回源 PostgreSQL，最后回填 Redis。

### 4.2 第一次查库存

```powershell
Invoke-RestMethod -Method Get -Uri 'http://127.0.0.1:<gateway-port>/api/inventory/sku-1'
```

预期：

- `InventoryService` 出现一次缓存未命中
- 然后出现 PostgreSQL 回源
- 最后回填 Redis

### 4.3 创建订单

```powershell
$body = '{"sku":"sku-1","quantity":2,"customerId":"customer-1"}'
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:<gateway-port>/api/orders' -ContentType 'application/json' -Body $body
```

预期：

- Gateway 产生入口 span
- OrderService 产生 `order.create`
- OrderService 通过 `HttpClient` 调用 InventoryService
- InventoryService 命中缓存或回源数据库
- OrderService 将订单写入 PostgreSQL

### 4.4 失败场景

```powershell
$body = '{"sku":"sku-3","quantity":2,"customerId":"customer-2"}'
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:<gateway-port>/api/orders' -ContentType 'application/json' -Body $body
```

预期：

- 返回库存不足
- `orders_failed_total` 增加
- Trace 中对应 span 为 error
- Loki 中能看到失败日志

## 5. 在 Grafana 里怎么看

### 5.1 看指标

进入：

```text
Dashboards -> 可观测性链路演示
```

建议先看：

- `业务服务总览`
- `按服务指标明细`
- `订单链路`
- `库存缓存视图`
- `Collector 管道`

### 5.2 看 Trace

进入：

```text
Explore -> Tempo
```

可按这些条件查：

- `service.name = gateway`
- `service.name = orderservice`
- `service.name = inventoryservice`

### 5.3 看日志

进入：

```text
Explore -> Loki
```

可先试：

```logql
{service_name="orderservice"}
```

或：

```logql
{service_name="inventoryservice"}
```

日志正文里带有：

- `TraceId=...`
- `Sku=...`
- `AvailableQuantity=...`

## 6. 验证命令

### 6.1 测试

```powershell
dotnet test .\tests\ObservabilityTraceDemo.OrderService.Tests\ -m:1 -p:UseSharedCompilation=false
dotnet test .\tests\ObservabilityTraceDemo.InventoryService.Tests\ -m:1 -p:UseSharedCompilation=false
```

### 6.2 构建

```powershell
dotnet build .\ObservabilityTraceDemo.sln -m:1 -p:UseSharedCompilation=false
```

### 6.3 Compose 配置校验

```powershell
cd .\infra\observability
docker compose config
```

## 7. 停止与清理

停止观测栈：

```powershell
cd E:\wfcodes\observability-trace-demo\infra\observability
docker compose down
```

清理观测栈数据卷：

```powershell
docker compose down -v
```

## 8. 推荐阅读

- [docs/observability-metrics-catalog.md](/E:/wfcodes/observability-trace-demo/docs/observability-metrics-catalog.md)
- [docs/observability-trace-walkthrough.md](/E:/wfcodes/observability-trace-demo/docs/observability-trace-walkthrough.md)
- [docs/observability-log-guide.md](/E:/wfcodes/observability-trace-demo/docs/observability-log-guide.md)
- [docs/production-observability-operations.md](/E:/wfcodes/observability-trace-demo/docs/production-observability-operations.md)
- [infra/README.md](/E:/wfcodes/observability-trace-demo/infra/README.md)
- [infra/observability/README.md](/E:/wfcodes/observability-trace-demo/infra/observability/README.md)
