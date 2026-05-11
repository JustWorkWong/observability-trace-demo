# OTel Collector 配置说明

文件：

- `[otel-collector-config.yml](E:/wfcodes/observability-trace-demo/infra/observability/otel-collector/otel-collector-config.yml)`

## 1. 它的作用

Collector 是这套平台的“路由中枢”。

它做三件事：

1. 接收业务服务发来的 OTLP 信号
2. 做批处理
3. 按信号类型分发到不同后端

## 2. 配置结构怎么读

### `receivers`

定义“从哪里收数据”。

当前只开了一个：

- `otlp`

它下面有两种协议：

- `grpc`
- `http`

含义：

- 业务服务可以用 OTLP gRPC 发数据
- 也预留了 OTLP HTTP 能力

### `processors`

定义“收到以后怎么处理”。

当前只开了：

- `batch`

关键参数：

- `timeout: 1s`
  - 最多等 1 秒就打包发一次
- `send_batch_size: 1024`
  - 单批最多攒 1024 条再发

作用：

- 减少太碎的发送
- 降低本地 demo 抖动

### `exporters`

定义“处理完发去哪儿”。

当前有三类：

- `otlp/tempo`
  - 发 Trace 到 Tempo
- `otlphttp/loki`
  - 发 Log 到 Loki
- `prometheus`
  - 暴露 Metric 给 Prometheus 抓

### `service.pipelines`

定义“哪类信号走哪条管道”。

当前分成三条：

- `traces`
- `logs`
- `metrics`

对应关系非常直白：

- traces -> tempo
- logs -> loki
- metrics -> prometheus exporter

## 3. 关键参数解释

### `endpoint: 0.0.0.0:4317`

含义：

- Collector 在容器内部监听 gRPC OTLP 入口

### `endpoint: 0.0.0.0:4318`

含义：

- Collector 在容器内部监听 HTTP OTLP 入口

### `tls.insecure: true`

含义：

- 本地 demo 环境不启 TLS 校验

为什么开：

- 降低本地演示复杂度

生产环境里一般不建议这么配。

### `resource_to_telemetry_conversion.enabled: true`

含义：

- 把 `service.name` 这类 Resource 标签转换成 Prometheus 标签

效果：

- 你在 Grafana / Prometheus 查询指标时，可以直接按服务名过滤

### `service.telemetry.metrics.address: 0.0.0.0:8888`

含义：

- Collector 自己也暴露内部指标

作用：

- 看 Collector 有没有收到 spans
- 看 exporter 有没有发送失败
- 看 processor 有没有积压

## 4. 普通用户最容易忽略的点

### Collector 不是存储

它只是中间层，不是最终存储。

真正存储的是：

- Tempo
- Loki
- Prometheus

### Metrics 不是“推到 Prometheus 数据库”

这里的做法是：

- 应用推给 Collector
- Collector 再暴露一个 `/metrics`
- Prometheus 去抓这个 `/metrics`

所以 Prometheus 仍然是拉模式。
