# 指标字典

这份文档说明本仓库里最值得关注的指标，以及生产环境里应该如何使用这些指标。

阅读顺序建议：

1. 先看入口和业务指标，判断用户是否受影响。
2. 再看依赖和运行时指标，判断慢或错发生在哪一层。
3. 最后看 Collector、Prometheus、Loki、Tempo 自身指标，确认监控平台没有丢数据。

## 1. 生产上优先盯什么

生产值班时，不建议一上来就看所有指标。指标越多，越容易误判。建议优先级如下：

| 优先级 | 指标方向 | 目的 |
| --- | --- | --- |
| P0 | 错误率、超时率、P95/P99、核心业务成功率 | 判断是否影响用户，是否需要升级事故 |
| P1 | 下单成功数、下单失败数、缓存命中率、下游调用耗时 | 判断业务是否变坏，定位影响范围 |
| P2 | GC、内存、线程池、连接池、Collector 导出失败 | 判断根因靠近应用、依赖，还是监控平台 |

常见排查顺序：

1. `gateway_requests_total` 和 `http_server_request_duration_seconds`：入口是否错误或变慢。
2. `orders_created_total` 和 `orders_failed_total`：核心业务是否正常落地。
3. `http_client_request_duration_seconds`：跨服务调用是否变慢。
4. `inventory_cache_hit_total` / `inventory_cache_miss_total`：缓存是否失效。
5. `otelcol_exporter_send_failed_*`：监控链路自身是否异常。

## 2. Gateway 指标

### `gateway_requests_total`

- 类型：`counter`
- 含义：网关完成转发的请求总数。
- 常见标签：
  - `gateway_route_group`：路由组，例如 `orders`、`inventory`。
  - `http_status_code`：HTTP 状态码。
- 生产用途：
  - 看入口流量是否到达正确业务路由。
  - 看 4xx / 5xx 是否突然升高。
  - 配合 `rate(...)` 看每秒请求量，不要直接看原始累计值。
- 常用 PromQL：

```promql
sum by (gateway_route_group) (rate(gateway_requests_total[5m]))
```

### `gateway_request_duration_seconds`

- 类型：`histogram`
- 含义：网关入口整体耗时，单位是秒。
- 生产用途：
  - 判断用户入口层是否变慢。
  - 对比下游服务耗时，判断慢在网关还是业务服务。
- 常用 PromQL：

```promql
histogram_quantile(
  0.95,
  sum by (le, gateway_route_group) (rate(gateway_request_duration_seconds_bucket[5m]))
)
```

### `gateway_upstream_failures_total`

- 类型：`counter`
- 含义：网关看到的上游失败总数。
- 生产用途：
  - 快速判断失败是否发生在网关转发到下游服务这一跳。
  - 如果它升高，同时业务服务没有对应请求量，优先检查服务发现、端口、网络和健康状态。

## 3. OrderService 指标

### `orders_created_total`

- 类型：`counter`
- 含义：订单创建成功总数。
- 生产用途：
  - 判断核心业务是否正常落地。
  - 当请求量正常但订单成功数下降时，说明业务链路内部可能失败。
- 注意：
  - 这个指标不带 `orderId`、`customerId`、`sku` 标签，避免高基数。

### `orders_failed_total`

- 类型：`counter`
- 含义：订单创建失败总数。
- 生产用途：
  - 判断失败趋势。
  - 和日志、trace 联动后定位失败原因，例如库存不足、库存服务失败、数据库写入失败。
- 常用 PromQL：

```promql
sum(rate(orders_failed_total[5m]))
```

### `order_create_duration_seconds`

- 类型：`histogram`
- 含义：一次下单从进入 OrderService 到返回结果的总耗时。
- 生产用途：
  - `P95` 用来看常规慢请求。
  - `P99` 用来看尾部延迟。
  - 如果它升高，再去 Tempo 里看具体 trace 的慢 span。
- 命名说明：
  - 时间指标统一使用 `_seconds`，这是 Prometheus 常见命名约定。

## 4. InventoryService 指标

### `inventory_cache_hit_total`

- 类型：`counter`
- 含义：库存查询缓存命中总数。
- 生产用途：
  - 判断 Redis 缓存是否有效承担读压力。
  - 冷启动后它通常会逐步升高。

### `inventory_cache_miss_total`

- 类型：`counter`
- 含义：库存查询缓存未命中总数。
- 生产用途：
  - 判断是否出现缓存穿透、缓存过期集中发生、热点迁移。
  - 如果 miss 突然升高，同时数据库耗时升高，要优先保护数据库。

### `inventory_lookup_duration_seconds`

- 类型：`histogram`
- 含义：库存查询总耗时。
- 生产用途：
  - 缓存命中时应该明显低于数据库回源。
  - 如果缓存命中率高但耗时仍然高，瓶颈可能在网络、序列化、Redis 本身或应用线程池。

### 缓存命中率

本仓库没有单独暴露一个命中率指标，而是用 hit / miss 计算：

```promql
sum(rate(inventory_cache_hit_total[5m]))
/
(
  sum(rate(inventory_cache_hit_total[5m]))
  +
  sum(rate(inventory_cache_miss_total[5m]))
)
```

## 5. 自动采集的 HTTP 指标

### `http_server_request_duration_seconds`

- 类型：`histogram`
- 来源：ASP.NET Core instrumentation。
- 含义：服务端入站 HTTP 请求耗时。
- 适用服务：
  - Gateway
  - OrderService
  - InventoryService
- 生产用途：
  - 按 `service_name` 区分服务。
  - 按 `http_response_status_code` 区分成功、客户端错误、服务端错误。

### `http_client_request_duration_seconds`

- 类型：`histogram`
- 来源：HttpClient instrumentation。
- 含义：出站 HTTP 请求耗时。
- 生产用途：
  - 看 `OrderService -> InventoryService` 是否变慢。
  - 看网关到下游服务的转发是否异常。

## 6. Runtime 指标

常见前缀：

- `process_*`
- `dotnet_*`

生产重点：

- GC 次数和暂停：判断是否存在内存压力或分配过高。
- 堆大小：判断内存是否持续上涨。
- 分配速率：判断热点代码是否制造大量短生命周期对象。
- 线程池队列：判断是否因为阻塞调用导致请求排队。
- 异常计数：判断是否有大量异常被捕获但没有变成 5xx。

注意：运行时指标通常用于解释“为什么慢”，不是第一时间判断“是否出事故”。

## 7. Collector 指标

### `otelcol_receiver_accepted_spans`

- 类型：`counter`
- 含义：Collector 接收到多少 trace span。
- 用途：
  - 判断应用是否真的把 trace 发到了 Collector。

### `otelcol_receiver_accepted_metric_points`

- 类型：`counter`
- 含义：Collector 接收到多少 metric points。
- 用途：
  - 判断应用指标是否进入采集链路。

### `otelcol_receiver_accepted_log_records`

- 类型：`counter`
- 含义：Collector 接收到多少 log records。
- 用途：
  - 判断应用日志是否进入采集链路。

### `otelcol_exporter_sent_*`

- 类型：`counter`
- 含义：Collector 成功导出的信号数量。
- 用途：
  - 判断 Collector 是否把数据成功送到 Tempo、Loki 或 Prometheus exporter。

### `otelcol_exporter_send_failed_*`

- 类型：`counter`
- 含义：Collector 导出失败数量。
- 用途：
  - 判断问题是否发生在 Collector 到后端平台这一段。
  - 如果它升高，Grafana 可能显示不完整数据。

## 8. Grafana 里怎么按服务区分

当前 dashboard 使用 `service_name` 作为业务服务过滤维度。

建议查看顺序：

1. `业务服务总览`：先选服务，确认请求量、错误率、P95。
2. `订单链路`：看订单成功、失败和下单耗时。
3. `库存缓存视图`：看缓存命中率和库存查询耗时。
4. `网关入口链路`：看入口转发状态。
5. `Collector 管道`：看观测链路自身是否丢数据。

约定：

- `service_name` 是最常用的服务维度。
- `job` 是 Prometheus 抓取任务维度，不等于业务服务名。
- `trace_id` 适合串联单次请求，不适合做 metric label。

## 9. 指标设计原则

生产上最容易出问题的是标签设计。

不要把这些字段放进 metric label：

- `trace_id`
- `span_id`
- `orderId`
- `customerId`
- 大量离散的 `sku`
- 原始 URL path 中的动态 ID

优先使用这些低基数字段：

- `service_name`
- `deployment_environment`
- `http_response_status_code`
- `http_route`
- `gateway_route_group`

一句话：指标用来判断趋势和聚合，不用来替代日志全文检索。

## 10. P95 / P99 是默认就有的吗

结论：P95 / P99 不是 Prometheus 里天然存在的独立指标，也不是 Grafana 自动生成的字段。

它们来自两步：

1. 应用或 instrumentation 暴露 histogram 指标，例如：
   - `http_server_request_duration_seconds_bucket`
   - `gateway_request_duration_seconds_bucket`
   - `order_create_duration_seconds_bucket`
   - `inventory_lookup_duration_seconds_bucket`
2. Grafana / Prometheus 用 PromQL 计算分位数，例如：

```promql
histogram_quantile(
  0.95,
  sum(rate(http_server_request_duration_seconds_bucket[5m])) by (le, service_name)
)
```

所以生产上要注意：

- 如果只暴露 counter，没有 bucket，就算不出 P95 / P99。
- ASP.NET Core HTTP 耗时指标由 OpenTelemetry ASP.NET Core instrumentation 自动提供 histogram。
- 本仓库的业务耗时指标由代码里的 `Meter.CreateHistogram<double>(...)` 显式创建。
- P95 / P99 的准确性依赖 bucket 分布、采样窗口和流量规模。低流量服务上 P99 很容易抖动。

## 11. 常见误区

### 11.1 直接看 Counter 原始值

Counter 只会上升。生产里一般看：

- `rate(counter[5m])`
- `increase(counter[1h])`

### 11.2 只看平均耗时

平均值会掩盖尾部慢请求。生产里更常看：

- P95
- P99
- 最大耗时样本对应的 trace

### 11.3 只看应用，不看观测平台

如果 Collector、Prometheus、Loki 或 Tempo 自身异常，Grafana 可能只是在显示“不完整的事实”。

### 11.4 把 demo 做法原样搬到生产

demo 为了教学会尽量多开信号。生产需要额外考虑：

- 采样
- retention
- cardinality
- 告警噪声
- 数据脱敏
- 成本
