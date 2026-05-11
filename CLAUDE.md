# Observability Trace Demo 架构说明

## 目录结构

```text
observability-trace-demo/
├─ ObservabilityTraceDemo.sln
├─ global.json
├─ CLAUDE.md
├─ README.md
├─ docs/
│  ├─ observability-log-guide.md
│  ├─ observability-metrics-catalog.md
│  └─ observability-trace-walkthrough.md
├─ infra/
│  ├─ README.md
│  └─ observability/
│     ├─ .env
│     ├─ README.md
│     ├─ docker-compose.yml
│     ├─ grafana/
│     ├─ loki/
│     ├─ otel-collector/
│     ├─ prometheus/
│     └─ tempo/
├─ src/
│  ├─ ObservabilityTraceDemo.AppHost/
│  ├─ ObservabilityTraceDemo.Gateway/
│  ├─ ObservabilityTraceDemo.InventoryService/
│  ├─ ObservabilityTraceDemo.OrderService/
│  └─ ObservabilityTraceDemo.ServiceDefaults/
└─ tests/
   ├─ ObservabilityTraceDemo.InventoryService.Tests/
   └─ ObservabilityTraceDemo.OrderService.Tests/
```

## 模块职责

- `src/ObservabilityTraceDemo.AppHost`
  - 本地业务拓扑编排器。
  - 拉起 `Gateway / OrderService / InventoryService / PostgreSQL / Redis`。
  - 保留 Aspire 注入的 `OTEL_EXPORTER_OTLP_ENDPOINT`，让服务把 telemetry 发回 Aspire Dashboard。
  - 额外下发 `OpenTelemetry__CollectorOtlpEndpoint`，让同一份 telemetry 进入外部 Collector / Grafana 栈。

- `src/ObservabilityTraceDemo.ServiceDefaults`
  - 共享的服务底座。
  - 统一封装服务发现、健康检查、OpenTelemetry Resource、日志关联、OTLP 导出。

- `src/ObservabilityTraceDemo.Gateway`
  - YARP 网关。
  - 负责把 `/api/orders/*` 转发到订单服务，把 `/api/inventory/*` 转发到库存服务。
  - 负责入口 span、转发日志、网关指标。

- `src/ObservabilityTraceDemo.OrderService`
  - 订单业务服务。
  - 负责下单、调用库存服务、落订单表、输出订单业务 span / metric / log。

- `src/ObservabilityTraceDemo.InventoryService`
  - 库存业务服务。
  - 负责 Redis 缓存读取、PostgreSQL 回源、库存种子初始化、输出缓存观测信号。

- `infra/observability`
  - 独立的观测基础设施目录。
  - 使用 Docker Compose 拉起 `Grafana / Prometheus / Loki / Tempo / OTel Collector`。
- `infra/README.md`
  - 给普通读者看的基础设施总览。
  - 解释为什么仓库要把平台层单独放进 `infra/`。

- `infra/observability/README.md`
  - 给普通读者看的观测平台总览。
  - 解释 `.env`、`docker-compose.yml`、Collector、Prometheus、Loki、Tempo、Grafana 各自扮演什么角色。

- `tests/*`
  - 核心业务行为测试。
  - 当前覆盖订单创建成败、库存缓存命中与回源逻辑。

## 依赖关系

```text
Client
  -> Gateway
      -> OrderService
          -> InventoryService
              -> Redis
              -> PostgreSQL (inventory schema)
          -> PostgreSQL (ordering schema)

OrderService / InventoryService / Gateway
  -> ServiceDefaults
  -> OTel Collector
      -> Tempo
      -> Loki
      -> Prometheus
          -> Grafana
```

## 数据边界

- PostgreSQL 只有一个实例、一个数据库。
- `OrderService` 只拥有 `ordering.orders`。
- `InventoryService` 只拥有 `inventory.stock_items`。
- Redis 只由 `InventoryService` 使用，采用 cache-aside。

## 观测链路

- `Gateway`
  - `gateway.route` ActivitySource
  - `gateway_requests_total`
  - `gateway_request_duration_seconds`

- `OrderService`
  - `order.create` ActivitySource
  - `orders_created_total`
  - `orders_failed_total`
  - `order_create_duration_seconds`

- `InventoryService`
  - `inventory.lookup`
  - `inventory.cache.read`
  - `inventory.cache.populate`
  - `inventory_cache_hit_total`
  - `inventory_cache_miss_total`
  - `inventory_lookup_duration_seconds`

- 自动采集
  - ASP.NET Core 入站请求
  - HttpClient 出站请求
  - EF Core / Npgsql 数据库访问
  - StackExchange.Redis 客户端调用
  - Runtime 指标

## 设计取舍

- 用显式 SQL 初始化 schema/table，而不是第一版就上 Migration：
  - 目标是让 demo 开箱即跑；
  - 同时保留“两个服务、两个 schema”的清晰所有权边界。

- 观测栈不放进 AppHost，而单独放 `infra/observability`：
  - AppHost 专注业务拓扑；
  - Compose 专注平台组件；
  - 这样更便于讲解 Collector 与后端的职责分离。
