# 指标字典

这份文档按“先业务指标，再平台指标”的顺序说明本仓库里最值得看的指标。

## 1. Gateway 指标

### `gateway_requests_total`

- 类型：`counter`
- 含义：网关完成转发的请求总数
- 常见标签：
  - `gateway_route_group`
  - `http_status_code`
- 看它解决什么问题：
  - 入口流量有没有打到正确路由
  - 某类接口的 4xx / 5xx 是否突然增多

### `gateway_request_duration_ms`

- 类型：`histogram`
- 含义：网关入口整体耗时
- 典型看法：
  - `p95` 高，但下游业务服务不高，可能是网关转发或上游等待问题
  - `p95` 与订单耗时一起涨，多半是下游真的慢了

### `gateway_upstream_failures_total`

- 类型：`counter`
- 含义：网关看到的上游失败总数
- 用途：
  - 快速判断问题是在网关层，还是下游服务已经在报错

## 2. OrderService 指标

### `orders_created_total`

- 类型：`counter`
- 含义：订单创建成功总数
- 看它解决什么问题：
  - 业务吞吐是否正常
  - 在有流量时，是否出现“请求进来了但订单没成功落地”

### `orders_failed_total`

- 类型：`counter`
- 含义：订单创建失败总数
- 常见原因：
  - 库存不足
  - 下游库存服务失败
  - 数据库写入失败

### `order_create_duration_ms`

- 类型：`histogram`
- 含义：一次下单从进入订单服务到返回结果的总耗时
- 推荐看法：
  - `p95`：常规慢请求
  - `p99`：极端尾延迟

## 3. InventoryService 指标

### `inventory_cache_hit_total`

- 类型：`counter`
- 含义：库存查询缓存命中总数
- 意义：
  - 命中越高，数据库压力越小
  - 首次冷启动后应该逐渐升高

### `inventory_cache_miss_total`

- 类型：`counter`
- 含义：库存查询缓存未命中总数
- 意义：
  - 冷启动、缓存过期、热点漂移时它会上升

### `inventory_lookup_duration_ms`

- 类型：`histogram`
- 含义：库存查询总耗时
- 观察重点：
  - 命中缓存时应明显低于回源数据库时
  - 如果命中率高但耗时仍高，说明瓶颈可能不在数据库

## 4. 自动采集的 HTTP 指标

### `http_server_request_duration_seconds`

- 类型：`histogram`
- 含义：ASP.NET Core 入站请求耗时
- 适用对象：
  - Gateway
  - OrderService
  - InventoryService

### `http_client_request_duration_seconds`

- 类型：`histogram`
- 含义：出站 HttpClient 调用耗时
- 在本仓库里主要看：
  - `OrderService -> InventoryService`
  - `Gateway -> 下游服务`

## 5. Runtime 指标

### 典型前缀

- `process_*`
- `dotnet_*`

### 建议重点看

- GC 次数和暂停
- 堆大小
- 分配速率
- 线程池队列
- 异常计数

这些指标回答的是：

- 代码慢，还是运行时压力大
- 是依赖慢，还是应用本身在 GC / 分配上出问题

## 6. Collector 指标

### `otelcol_receiver_accepted_spans`

- 类型：`counter`
- 含义：Collector 收到了多少 trace span

### `otelcol_receiver_accepted_metric_points`

- 类型：`counter`
- 含义：Collector 收到了多少 metric 点

### `otelcol_receiver_accepted_log_records`

- 类型：`counter`
- 含义：Collector 收到了多少 log record

### `otelcol_exporter_sent_*`

- 类型：`counter`
- 含义：成功导出的信号数量
- 用途：
  - 判断 Collector 是不是收到了，却没发出去

### `otelcol_exporter_send_failed_*`

- 类型：`counter`
- 含义：导出失败数量
- 用途：
  - 看问题是出在应用侧、Collector 侧，还是后端平台侧

## 7. 在 Grafana 里怎么查

### 看业务吞吐

- `Order Flow`
- `System Overview`

### 看缓存命中率

- `Inventory Cache View`

### 看平台是否吞数据

- `Collector Pipeline`

### 看入口错误率

- `Gateway Trace Entry`

## 8. 指标设计原则

- 不把 `trace_id` / `span_id` 放进 metric label
- 不把 `customerId` / `orderId` / `sku` 大量离散值无脑塞进 label
- 优先用：
  - route group
  - status code
  - service name
  - environment

一句话：指标拿来做“趋势判断”，不是拿来替代日志全文检索。
