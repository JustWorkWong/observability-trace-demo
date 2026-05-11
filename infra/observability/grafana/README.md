# Grafana 配置说明

Grafana 目录包含两类东西：

```text
grafana/
├─ dashboards/
└─ provisioning/
```

## 1. 它的作用

Grafana 是统一展示层。

它不负责存储 Trace / Log / Metric。

它负责：

- 连接不同数据源
- 把它们画成 dashboard
- 提供 Explore 查询
- 提供 trace -> log / log -> trace 跳转体验

## 2. `dashboards/` 是什么

这里放的是仪表盘 JSON。

每个 JSON 本质上都是：

- 面板定义
- 查询表达式
- 布局信息

建议你把它理解成：

> “Grafana 仪表盘快照文件”

## 3. `provisioning/` 是什么

这里放的是 Grafana 启动时自动装配的配置。

目的：

- 不用手工去 UI 一步步创建 datasource
- 不用手工导入 dashboard

## 4. 当前预置的 dashboard

- `业务服务总览`
- `按服务指标明细`
- `网关入口链路`
- `订单链路`
- `库存缓存视图`
- `Collector 管道`

## 5. 为什么 Grafana 不像 Aspire 自动按服务分组

Aspire Dashboard 是面向 .NET Aspire resource 的本地开发 UI，
它天然知道 `gateway`、`orderservice`、`inventoryservice` 是三个应用资源。

Grafana 是通用观测平台。Prometheus 抓取本仓库指标时，抓到的是
`otel-collector:9464` 这个统一出口，所以：

- `job` 是 `collector-app-metrics`
- 真正的业务服务名在 `service_name` 标签里

因此 Grafana 可以按服务看，但 dashboard 和 PromQL 必须显式使用
`service_name`。

本仓库提供两个服务视角：

- `业务服务总览`
  - 横向比较 gateway / orderservice / inventoryservice。
- `按服务指标明细`
  - 先选择一个服务，再查看该服务的 HTTP、依赖、数据库、Runtime 指标。

## 6. 普通用户最关心的问题

### 为什么 Grafana 里能同时看到 Trace / Log / Metric

因为它同时连了三种 datasource：

- Prometheus
- Loki
- Tempo

### 为什么能从日志跳 Trace

因为 Loki datasource 配了 `derivedFields`

### 为什么能从 Trace 跳日志

因为 Tempo datasource 配了 `tracesToLogsV2`
