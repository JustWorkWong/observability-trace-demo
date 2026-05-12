# 生产可观测性与运维手册

这份文档不讲“怎么把 Grafana 点开”，而讲生产环境里真正要盯什么、为什么盯、以及新手最容易误解什么。

## 1. 体系：生产上先按层看，而不是先按工具看

生产环境的可观测性，建议按这五层去建立视角：

### 1.1 用户与入口层

核心问题：

- 用户是否还能正常访问
- 请求是否成功
- 请求是否变慢

重点指标：

- 请求量
- 错误率
- P95 / P99
- 超时率

对应到本仓库：

- `gateway_requests_total`
- `gateway_request_duration_seconds`
- `http_server_request_duration_seconds`

### 1.2 业务服务层

核心问题：

- 功能是否按预期完成
- 业务失败是偶发还是趋势

重点指标：

- 成功数
- 失败数
- 业务耗时

对应到本仓库：

- `orders_created_total`
- `orders_failed_total`
- `order_create_duration_seconds`

### 1.3 依赖层

核心问题：

- 慢是不是因为下游依赖
- 缓存是否失效
- 数据库是否成为瓶颈

重点指标：

- HttpClient 调用耗时
- Redis 命中率
- 数据库耗时
- 连接池状态

对应到本仓库：

- `http_client_request_duration_seconds`
- `inventory_cache_hit_total`
- `inventory_cache_miss_total`
- `inventory_lookup_duration_seconds`

### 1.4 运行时层

核心问题：

- 应用慢是业务逻辑问题，还是 GC / 内存 / 线程池问题

重点指标：

- GC 次数
- 堆大小
- 分配速率
- 线程池排队
- 异常计数

### 1.5 观测平台层

核心问题：

- 数据到底有没有进来
- Collector 有没有堵住
- 平台有没有只展示了“部分真相”

重点指标：

- `otelcol_receiver_accepted_*`
- `otelcol_exporter_sent_*`
- `otelcol_exporter_send_failed_*`
- Prometheus targets 是否 `up`

## 2. 生产上真正应该优先关注哪些指标

不是所有指标都同样重要。生产值班时，建议优先级如下。

### 第一优先级：先看会不会影响用户

- 请求错误率
- 请求耗时 P95 / P99
- 超时数
- 核心业务成功率

这是最先决定要不要升级为事故的指标。

### 第二优先级：再看业务是不是在变坏

- 成功订单数
- 失败订单数
- 库存命中率
- 热点接口吞吐

### 第三优先级：最后看根因靠近哪里

- `HttpClient` 慢不慢
- Redis 命中率是否下降
- 数据库 span 是否拉长
- Collector 是否导出失败

## 3. 必备的生产运维知识

### 3.1 Grafana 不是数据库

Grafana 只负责：

- 查
- 画
- 跳转

真正存储数据的是：

- Prometheus
- Loki
- Tempo

### 3.2 Prometheus 是拉模式

在这套设计里：

- 业务服务先把指标推给 Collector
- Collector 再暴露 `/metrics`
- Prometheus 去抓这个 `/metrics`

所以最终进入 Prometheus 的动作，仍然是“抓取”。

### 3.3 Counter 不能直接看原始值判断趋势

像：

- `orders_created_total`
- `gateway_requests_total`

这种 Counter，只会上升。

生产看法应该是：

- `rate(...)`
- `increase(...)`

而不是盯绝对值。

### 3.4 P95 / P99 不是平均值

- 平均值容易掩盖尾延迟
- P95 / P99 更能暴露用户真实感受到的慢请求

### 3.5 Trace 不是越多越好

生产环境里需要考虑：

- 采样
- 成本
- 存储保留

本地 demo 可以多开，但生产要按价值采。

### 3.6 日志标签不能乱打

Loki 和 Prometheus 一样，都怕高基数。

不要把这些字段直接当标签打：

- userId
- orderId
- sku（如果取值非常多）
- traceId

这些更适合放在日志正文或结构化字段里，而不是做索引标签。

### 3.7 指标标签也不能乱打

Prometheus 官方非常明确：

- 高基数标签会迅速放大时间序列数量

所以这轮我已经把自定义业务指标里的 `sku` 标签去掉了，避免示例误导到生产做法。

## 4. 易错点

### 4.1 把 demo 指标设计直接搬进生产

这是最常见的误区。

demo 为了教学会更直观，生产要优先考虑：

- cardinality
- retention
- 查询成本
- 告警稳定性

### 4.2 只做 dashboard，不做告警

面板是“看见问题”。

告警是“问题发生时能不能叫醒人”。

生产里不能只有 dashboard。

### 4.3 只看平均值，不看尾延迟

平均值好看，不代表用户没有慢请求。

### 4.4 只看 5xx，不看 4xx

有些生产问题会表现成：

- 429
- 401/403 激增
- 400 激增（客户端或网关协议不兼容）

4xx 不一定是“可以忽略”。

### 4.5 没有 runbook

指标和日志只是告诉你“出了什么问题”。

runbook 才告诉你“接下来该做什么”。

### 4.6 把日志当指标，把指标当日志

- 指标适合看趋势
- 日志适合看个案
- Trace 适合看链路

混用会让排障效率很差。

## 5. 约定俗成，但新手常常不知道的事

### 5.1 `service.name` 是最常用的服务维度

大多数 dashboard、trace、log 过滤，第一维通常就是：

- `service.name`

### 5.2 `service.namespace` 是“把同一套系统归组”的手段

它不是必须，但在多系统共用平台时非常有用。

### 5.3 `job` 和 `service.name` 不是一回事

在 Prometheus 里：

- `job` 更接近抓取任务视角
- `service.name` 更接近应用资源视角

生产排查时两者都常见，不能混着理解。

### 5.4 `up == 1` 不代表业务正常

它只代表：

> 抓取目标还活着

不代表：

- 业务成功
- 数据库正常
- 下游依赖正常

### 5.5 `TraceId` 适合串联，不适合做聚合维度

TraceId 的价值是：

- 用来定位单次请求

不是：

- 用来做 Prometheus 标签聚合

### 5.6 `dashboard 好看` 不等于 `可运维`

一个真正能值班用的面板，至少要回答：

1. 现在是不是出事了
2. 影响哪一层
3. 影响哪些服务
4. 先去看日志、trace，还是先扩容

## 6. 当前这套仓库里的生产化建议

如果把这个 demo 往生产化方向推进，优先顺序建议是：

1. 固定镜像版本
2. 完整认证与权限
3. 指标标签瘦身
4. Trace 采样策略
5. 日志脱敏
6. 告警规则
7. 数据保留策略
8. runbook
9. SLI / SLO / error budget
10. 平台自身高可用

部署配置请继续看：

- [production-deployment-configuration.md](/E:/wfcodes/observability-trace-demo/docs/production-deployment-configuration.md)
