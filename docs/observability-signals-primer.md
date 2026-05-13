# 可观测性入门：Trace、Metric、Log 与 P95/P99

这份文档面向刚开始接触可观测性的读者，目标不是背术语，而是建立一套排查问题时不会走偏的心智模型。

一句话先记住：

```text
Metric 负责发现问题，Trace 负责定位原因，Log 负责补充证据。
```

## 1. 三类信号分别回答什么问题

| 信号 | 中文理解 | 最擅长回答 | 本项目后端 |
| --- | --- | --- | --- |
| Metric | 数字时间序列 | 最近一段时间整体是否变慢、错误率是否升高、哪个接口最差 | Prometheus |
| Trace | 单次请求链路 | 这一次请求经过哪些服务、哪一段最慢、错误发生在哪个 span | Tempo |
| Log | 文本事件记录 | 具体异常、业务原因、SQL 文本、参数和上下文是什么 | Loki |

常见排查顺序：

1. 先看 Metric：确认是整体趋势问题，还是单个请求问题。
2. 再看 Trace：找到慢请求或错误请求的完整链路。
3. 最后看 Log：补充业务细节、异常堆栈、SQL 文本和关键参数。

不要把三者互相替代：

- 不要用 Log 扫描来长期统计 P95/P99。
- 不要用 Trace 当全局排行榜。
- 不要指望 Metric 告诉你某一次请求的完整上下文。

## 2. Metric：趋势、排名和告警

Metric 是带时间戳的数字数据。它适合做图、排行榜和告警。

本项目里常见的 metric 有：

- `http_server_request_duration_seconds`：ASP.NET Core 入站 HTTP 请求耗时。
- `http_client_request_duration_seconds`：HttpClient 出站请求耗时。
- `gateway_request_duration_seconds`：网关转发耗时。
- `order_create_duration_seconds`：订单创建业务耗时。
- `inventory_lookup_duration_seconds`：库存查询业务耗时。
- `orders_created_total`：订单创建成功总数。
- `orders_failed_total`：订单创建失败总数。
- `inventory_cache_hit_total` / `inventory_cache_miss_total`：缓存命中和未命中次数。

Metric 能回答的问题：

- 最近 5 分钟哪个接口 P95/P99 最高。
- Gateway、OrderService、InventoryService 哪个服务错误率上升。
- 下单接口 QPS 是否下降。
- 库存缓存命中率是否变差。
- PostgreSQL 或 Redis 相关耗时是否上升。

Metric 不适合回答的问题：

- 某一次具体请求为什么慢。
- 某个用户这一次下单经过了哪些服务。
- 某条具体 SQL 的参数是什么。

这些问题应该交给 Trace 和 Log。

## 3. Trace：单次请求的调用链

Trace 表示一次完整请求链路。一次下单请求可能长这样：

```text
Gateway
  -> OrderService
      -> InventoryService
          -> Redis
          -> PostgreSQL
      -> PostgreSQL
```

Trace 由多个 span 组成。

| 名词 | 含义 |
| --- | --- |
| Trace | 一次完整请求链路 |
| Span | 链路中的一段工作，例如一次 HTTP 调用、一次 SQL 查询、一次缓存读取 |
| TraceId | 整条链路的 ID，同一次请求里的 span 共享同一个 TraceId |
| SpanId | 单个 span 自己的 ID |
| Parent Span | 当前 span 的上游调用 |
| Child Span | 当前 span 触发的下游调用 |
| Span Duration | 某一段工作的耗时 |
| Span Attribute | span 上的结构化字段，例如 `db.system=postgresql` |

Trace 能回答的问题：

- 慢请求卡在 Gateway、OrderService、InventoryService、Redis 还是 PostgreSQL。
- 是服务间 HTTP 调用慢，还是数据库查询慢。
- 错误 span 在哪一层。
- 这次请求是否触发了缓存未命中。

Trace 不适合做长期统计。比如“最近 15 分钟哪个接口整体最慢”，应该先用 Metric 查排行榜，再用 Trace 看样本。

## 4. Log：文本证据和业务上下文

Log 是应用主动写出的事件。它适合保存人能读懂的上下文。

本项目日志里重点关注：

- `TraceId`：用于从日志跳回 trace。
- `Sku`、`Quantity`、`CustomerId`：业务排查字段。
- `AvailableQuantity`：库存不足时的上下文。
- 异常信息：判断失败原因。

Log 能回答的问题：

- 这次订单为什么失败。
- 具体 SKU 和库存数量是什么。
- SQL 文本或异常堆栈是什么。
- 业务是否命中了某个特殊分支。

Log 不适合做主要统计系统。长期看 QPS、错误率、P95/P99 时，Metric 更便宜、更稳定、更适合告警。

## 5. Percentile、P95、P99 是什么

Percentile 叫百分位数。它表示一批请求耗时的分布位置。

| 名词 | 含义 |
| --- | --- |
| P50 | 50% 的请求耗时小于等于这个值，也叫中位数 |
| P95 | 95% 的请求耗时小于等于这个值，剩下 5% 更慢 |
| P99 | 99% 的请求耗时小于等于这个值，剩下 1% 更慢 |
| Average | 平均值，容易掩盖尾部慢请求 |
| Max | 最大值，容易被极端个例影响 |

例子：

```text
P95 = 300ms
```

它的意思是：最近这批请求里，95% 的请求耗时小于等于 300ms。

它不是这些意思：

- 不是 95% 的概率请求一定等于 300ms。
- 不是最慢请求。
- 不是平均耗时。

为什么线上常看 P95/P99，而不是只看平均值：

- 平均值会把少量慢请求摊平。
- P95 更能代表大多数用户里的慢体验。
- P99 更能观察尾部延迟，适合发现偶发但严重的慢请求。

也要注意：

- 低流量接口的 P99 很容易抖动。
- 只看 P99 不看请求量，容易误判。
- P99 很高时，需要结合 Trace 看具体慢样本。

## 6. Histogram、Bucket、Quantile 是什么

Histogram 叫直方图。它不是保存每一次请求明细，而是把请求耗时放进不同的桶里。

比如一批请求耗时被统计成：

```text
<= 50ms: 120 次
<= 100ms: 300 次
<= 300ms: 900 次
<= 1s: 980 次
<= 5s: 1000 次
```

这里的 `50ms`、`100ms`、`300ms`、`1s`、`5s` 就是 bucket 边界。

Prometheus 里常见的 histogram 会暴露三类时间序列：

| 后缀 | 含义 |
| --- | --- |
| `_bucket` | 每个桶里的累计数量，用于计算分位数 |
| `_sum` | 所有样本值之和，可用于计算平均值 |
| `_count` | 样本总数，可用于计算请求量 |

例如：

```text
http_server_request_duration_seconds_bucket
http_server_request_duration_seconds_sum
http_server_request_duration_seconds_count
```

Quantile 是分位数。`0.95 quantile` 就是 P95，`0.99 quantile` 就是 P99。

在 Prometheus 中，P95/P99 通常不是应用直接上报的独立指标，而是用 `histogram_quantile(...)` 从 histogram bucket 计算出来。

## 7. Counter、Gauge、Histogram 的区别

| 类型 | 特点 | 例子 | 常见查询方式 |
| --- | --- | --- | --- |
| Counter | 只增不减 | 请求总数、错误总数、订单创建总数 | `rate(...)`、`increase(...)` |
| Gauge | 可增可减 | 当前内存、队列长度、当前连接数 | 直接看当前值或趋势 |
| Histogram | 统计分布 | 请求耗时、SQL 耗时、业务操作耗时 | `histogram_quantile(...)` |

容易出错的地方：

- Counter 不要直接看原始值，因为它一直增长。
- 请求量一般看 `rate(counter[5m])`。
- P95/P99 必须基于 histogram bucket 计算。
- Gauge 适合表达当前状态，不适合表达累计次数。

## 8. Label、Attribute、Cardinality 是什么

Label、Tag、Attribute 都可以理解为维度字段。不同系统叫法不同，但心智模型类似。

例子：

```text
service_name="orderservice"
http_route="/api/orders/{id}"
http_response_status_code="500"
deployment_environment="Development"
```

这些字段让我们可以按服务、接口、状态码、环境聚合。

Cardinality 叫基数，表示维度组合数量。它是可观测性系统里最容易被低估的成本来源。

好的低基数字段：

- `service_name`
- `http_route`
- `http_request_method`
- `http_response_status_code`
- `deployment_environment`
- `gateway_route_group`

危险的高基数字段：

- `trace_id`
- `span_id`
- `orderId`
- `customerId`
- 原始 URL，例如 `/api/orders/123456`
- 大量离散的 SKU

关键约定：

```text
Metric label 里放低基数字段。
Trace 和 Log 里放高细节上下文。
```

也就是说，`customerId` 不适合做 metric label，但可以放到 log 或 trace attribute 里用于个案排查。

## 9. “哪个接口慢”到底看 Metric 还是 Trace

先看 Metric，再看 Trace。

Metric 用来回答：

- 哪个接口最近 5 分钟 P95 最高。
- 哪个接口最近 15 分钟 P99 最高。
- 哪个服务的错误率升高。
- 哪个依赖调用耗时变差。

Trace 用来回答：

- 这个慢请求具体慢在哪个 span。
- 是 SQL 慢、Redis 慢、HTTP 下游慢，还是业务代码慢。
- 这次请求的上下游关系是什么。
- 这次错误是哪个服务抛出的。

标准排查路径：

```text
Metric 排名发现慢接口
  -> 打开对应时间窗口
  -> 找慢 trace 样本
  -> 看最长 span
  -> 跳到 log 查业务证据
```

## 10. 本项目中接口 P95/P99 是默认的吗

结论：不是 Grafana 自动生成的独立字段，但本项目已经具备计算基础。

本项目有两类耗时 histogram：

1. OpenTelemetry ASP.NET Core instrumentation 自动采集的 HTTP 请求耗时。
2. 代码里用 `Meter.CreateHistogram<double>(...)` 显式创建的业务耗时。

对应位置：

- `src/ObservabilityTraceDemo.ServiceDefaults/Extensions.cs`：开启 ASP.NET Core、HttpClient、Runtime metrics。
- `src/ObservabilityTraceDemo.Gateway/Observability/GatewayTelemetry.cs`：定义网关耗时 histogram。
- `src/ObservabilityTraceDemo.OrderService/Observability/OrderTelemetry.cs`：定义订单创建耗时 histogram。
- `src/ObservabilityTraceDemo.InventoryService/Observability/InventoryTelemetry.cs`：定义库存查询耗时 histogram。

所以：

- 请求耗时样本会进入 histogram。
- Prometheus 会保存 bucket。
- Grafana 用 PromQL 从 bucket 计算 P50/P95/P99。

P95/P99 不是单独上报的值，而是查询时算出来的结果。

## 11. 常用 PromQL

按服务看最近 5 分钟 HTTP P95：

```promql
histogram_quantile(
  0.95,
  sum by (le, service_name) (
    rate(http_server_request_duration_seconds_bucket[5m])
  )
)
```

按接口看最近 5 分钟 HTTP P95：

```promql
histogram_quantile(
  0.95,
  sum by (le, service_name, http_route) (
    rate(http_server_request_duration_seconds_bucket[5m])
  )
)
```

按接口看最近 5 分钟 HTTP P99：

```promql
histogram_quantile(
  0.99,
  sum by (le, service_name, http_route) (
    rate(http_server_request_duration_seconds_bucket[5m])
  )
)
```

接口 QPS：

```promql
sum by (service_name, http_route) (
  rate(http_server_request_duration_seconds_count[5m])
)
```

接口 5xx 错误率：

```promql
sum by (service_name, http_route) (
  rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m])
)
/
sum by (service_name, http_route) (
  rate(http_server_request_duration_seconds_count[5m])
)
```

订单创建 P95：

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(order_create_duration_seconds_bucket[5m])
  )
)
```

库存查询 P95：

```promql
histogram_quantile(
  0.95,
  sum by (le) (
    rate(inventory_lookup_duration_seconds_bucket[5m])
  )
)
```

缓存命中率：

```promql
sum(rate(inventory_cache_hit_total[5m]))
/
(
  sum(rate(inventory_cache_hit_total[5m]))
  +
  sum(rate(inventory_cache_miss_total[5m]))
)
```

注意：不同 OpenTelemetry 版本和 Prometheus 导出器可能会让 label 名略有差异。以 Grafana Explore 中实际出现的 label 为准。

## 12. 慢 SQL 应该怎么观测

慢 SQL 最好由三类信号一起完成：

| 目标 | 首选信号 | 原因 |
| --- | --- | --- |
| 数据库整体是否变慢 | Metric | 看趋势、分位数、告警 |
| 某次请求是哪条 SQL 慢 | Trace | DB span 能放在完整请求链路里 |
| SQL 文本和异常细节 | Log 或 Trace attribute | 需要人读的上下文 |

推荐排查路径：

1. Metric 发现接口 P95/P99 升高。
2. Trace 找到慢请求样本。
3. 在 trace 里展开数据库 span。
4. 看 `db.system`、`db.operation`、`db.statement` 和 span duration。
5. 必要时去 Loki 查同一个 `TraceId` 的日志。

生产注意事项：

- SQL 文本可能包含敏感信息，要评估脱敏策略。
- 不要把完整 SQL 或参数放进 metric label。
- 慢 SQL 告警更适合基于 metric，根因定位更适合 trace。

## 13. Sampling、Retention、SLO 是什么

| 名词 | 含义 | 为什么重要 |
| --- | --- | --- |
| Sampling | 采样，不保存全部 trace | 降低 trace 成本 |
| Retention | 数据保留时间 | 决定能回查多久 |
| SLO | 服务目标，例如 99% 请求小于 500ms | 把观测指标变成工程目标 |
| Alert | 告警 | 当指标越过阈值时通知人 |
| Dashboard | 看板 | 把关键指标组织成稳定视图 |

生产环境常见做法：

- Metric 通常保留更久，用于趋势和容量分析。
- Trace 通常采样保存，优先保留错误和慢请求。
- Log 根据业务和合规要求保留，注意脱敏和成本。
- 告警优先基于 Metric，不优先基于全文日志扫描。

## 14. 新手最容易理解错的点

| 误解 | 正确认知 |
| --- | --- |
| Metric 只能看整体 | Metric 可以按低基数维度聚合到服务、接口、状态码 |
| Trace 可以替代 Metric | Trace 适合个案分析，不适合做长期全局统计 |
| Log 最详细，所以应该先看 Log | 慢接口和错误率先看 Metric 更快 |
| P99 是最慢请求 | P99 后面还有最慢的 1% |
| Average 能代表用户体验 | 平均值经常掩盖尾部慢请求 |
| P95/P99 是默认字段 | 它们通常由 histogram bucket 查询计算出来 |
| label 越多越好 | 高基数 label 会显著增加成本和系统压力 |
| trace_id 可以放 metric label | trace_id 应该用于 Trace/Log 关联，不适合 metric label |

## 15. 一套稳定的排查口诀

```text
先看 Metric：是不是整体变坏，哪个接口最差。
再看 Trace：这一次慢在哪里，哪个 span 最长。
最后看 Log：具体业务原因、异常和参数是什么。
```

对于本项目，推荐从 Grafana 这样进入：

1. `Dashboards -> 可观测性链路演示`：看服务、接口、业务指标。
2. `Explore -> Prometheus`：手写 PromQL 查 P95/P99、QPS、错误率。
3. `Explore -> Tempo`：查慢请求 trace。
4. `Explore -> Loki`：按 `TraceId` 查日志证据。

这套分工稳定之后，可观测性就不会变成“到处点一点试试看”，而是有路径、有证据、有结论。
