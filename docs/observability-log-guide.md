# 日志使用手册

## 1. 日志的定位

在这套仓库里：

- `Trace` 负责回答“请求经过了哪些步骤”
- `Metric` 负责回答“整体趋势是不是变坏了”
- `Log` 负责回答“具体为什么失败”

所以日志不是孤立看的，而是和 Trace、Metric 配套看的。

## 2. 关键日志来源

### Gateway

关键字段：

- `Method`
- `Path`
- `RouteGroup`
- `StatusCode`
- `DurationMs`
- `TraceId`

### OrderService

关键事件：

- 开始创建订单
- 库存不足
- 订单创建成功

关键字段：

- `Sku`
- `Quantity`
- `CustomerId`
- `OrderId`
- `TraceId`

### InventoryService

关键事件：

- 缓存命中
- 缓存未命中
- 数据库回源
- 种子写入完成

关键字段：

- `Sku`
- `AvailableQuantity`
- `TraceId`

## 3. 先看日志还是先看 Trace

### 先看日志的情况

- 已知报错，但不知道业务上下文
- 想快速确认某个 SKU 或订单是否出过问题
- 想看具体错误原因和参数

### 先看 Trace 的情况

- 已知请求变慢
- 想知道慢在哪一跳
- 想看缓存命中还是数据库回源

### 先看 Metric 的情况

- 想先判断问题是个例还是整体趋势
- 想看错误率、吞吐、命中率是否整体变化

## 4. Loki 查询示例

查询订单服务：

```logql
{service_name="orderservice"}
```

查询库存服务：

```logql
{service_name="inventoryservice"}
```

按 TraceId 过滤：

```logql
{service_name="orderservice"} |= "TraceId="
```

## 5. 从日志反查 Trace

我们在关键业务日志里显式打印了：

```text
TraceId=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Grafana 的 Loki datasource 已配置 derived field，会尝试把这个字段识别出来并跳到 Tempo。

如果没有自动跳转，也可以手工复制 TraceId 到 Tempo Explore 里查。

## 6. 结构化日志原则

- 关键业务参数用结构化字段，不拼字符串
- 保留 `TraceId`
- 保留失败原因
- 不把敏感信息写进日志

## 7. 常见误区

### 只看日志，不看 Trace

你会知道“失败了”，但不容易知道“前面走过哪些步骤”。

### 只看 Trace，不看日志

你会知道“失败发生在哪一层”，但不一定知道“具体业务原因”。

### 拿日志替代指标

日志适合查个案，不适合看整体趋势。
