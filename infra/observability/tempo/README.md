# Tempo 配置说明

文件：

- `[tempo-config.yml](E:/wfcodes/observability-trace-demo/infra/observability/tempo/tempo-config.yml)`

## 1. 它的作用

Tempo 是 Trace 存储后端。

它负责：

- 接收 Trace
- 存储 Trace
- 让 Grafana 查询 Trace

## 2. 关键结构

### `server`

定义 Tempo 自己的 HTTP 服务端口。

### `distributor.receivers.otlp`

定义 Tempo 从哪里接收 OTLP Trace。

这里同时保留：

- `grpc`
- `http`

但当前主链路里，Collector 主要走 gRPC 发 Trace 给 Tempo。

### `storage.trace.backend: local`

作用：

- 本地文件存储 Trace

适合 demo，简单直接。

### `compactor.compaction.block_retention: 24h`

作用：

- Trace 数据本地保留 24 小时

这对 demo 来说足够了。

### `metrics_generator`

作用：

- 支撑 Grafana 里的 TraceQL metrics 查询。
- 典型查询是：

```traceql
{ resource.service.name != nil } | rate() by(resource.service.name)
```

这类查询不是直接从普通 trace block 里简单读取，而是需要 Tempo 的
`metrics-generator` 参与。

本仓库打开了：

- `ring.kvstore.store: inmemory`
  - 本地单实例 Tempo 使用内存 ring。
  - 如果不配置 generator ring，Grafana 会报 `empty ring`。
- `processor.local_blocks`
  - 让 Tempo 为近期 trace 生成可查询的本地 metrics block。
  - 这是 `rate()`、`count_over_time()`、`quantile_over_time()` 这类 TraceQL metrics 函数的基础。
- `traces_storage.path`
  - local blocks 使用的本地 trace WAL 目录。
- `storage.path`
  - metrics-generator 自身的 WAL 目录。

### `overrides.metrics_generator_processors`

作用：

- 真正激活 metrics-generator processor。

当前配置：

```yaml
overrides:
  defaults:
    metrics_generator:
      processors: ["local-blocks"]
```

如果少了这一段，普通 trace 查询仍可能正常，但 Grafana 中基于 TraceQL metrics
的服务速率、按服务聚合、trace drilldown 统计会失败。

## 3. 为什么宿主机不一定暴露 Tempo 端口

在这套配置里，Tempo 更偏平台内部服务：

- Collector 需要访问它
- Grafana 需要访问它

但宿主机上的用户不一定非要直接访问它。

所以把它留在容器内部网络里更干净，也更少端口冲突。
