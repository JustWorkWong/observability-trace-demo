# Grafana Dashboard Provisioning 说明

文件：

- `[dashboards.yml](E:/wfcodes/observability-trace-demo/infra/observability/grafana/provisioning/dashboards/dashboards.yml)`

## 1. 它的作用

定义 Grafana 启动时去哪里加载 dashboard JSON。

## 2. 关键参数解释

### `folder`

决定这些 dashboard 在 Grafana 里属于哪个文件夹。

当前值：

- `可观测性链路演示`

### `type: file`

表示从文件系统读取 dashboard。

### `updateIntervalSeconds: 10`

表示 Grafana 每 10 秒检查一次 dashboard 文件有没有变化。

适合本地开发调试。

### `path`

Grafana 容器里 dashboard 文件的挂载路径。
