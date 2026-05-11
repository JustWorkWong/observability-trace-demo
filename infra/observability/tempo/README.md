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

## 3. 为什么宿主机不一定暴露 Tempo 端口

在这套配置里，Tempo 更偏平台内部服务：

- Collector 需要访问它
- Grafana 需要访问它

但宿主机上的用户不一定非要直接访问它。

所以把它留在容器内部网络里更干净，也更少端口冲突。
