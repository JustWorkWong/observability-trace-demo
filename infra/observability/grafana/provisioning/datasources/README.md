# Grafana Datasource Provisioning 说明

文件：

- `[datasources.yml](E:/wfcodes/observability-trace-demo/infra/observability/grafana/provisioning/datasources/datasources.yml)`

## 1. 它的作用

定义 Grafana 启动时自动创建哪些数据源。

当前包括：

- Prometheus
- Loki
- Tempo

## 2. 关键参数解释

### `name`

Grafana UI 里显示的名字。

### `uid`

Grafana 内部用来跨配置引用的稳定标识。

例如：

- Loki 要跳 Tempo，就要引用 Tempo 的 `uid`

### `url`

Grafana 在容器网络中访问目标服务的地址。

注意这里不是宿主机地址，而是容器间地址，例如：

- `http://prometheus:9090`

### `isDefault: true`

表示默认数据源。

当前默认给了 Prometheus。

### `derivedFields`

作用：

- 从日志里提取某个字段
- 再拿它拼成跳转参数

这里主要用来从 Loki 日志正文里的 `TraceId=...` 跳 Tempo。

### `tracesToLogsV2`

作用：

- 从 Tempo 的某个 trace/span 反查 Loki 日志

它是 trace -> log 跳转的关键。
