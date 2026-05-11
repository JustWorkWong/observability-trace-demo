# Observability 基础设施说明

这个目录不是“某一个配置文件的集合”，而是整套本地可观测性平台的装配说明。

你可以把它理解成：

> 让业务服务的 `Trace / Metric / Log` 有地方可去、有地方可存、有地方可看。

## 1. 目录结构

```text
observability/
├─ .env
├─ docker-compose.yml
├─ grafana/
├─ loki/
├─ otel-collector/
├─ prometheus/
└─ tempo/
```

## 2. 整体数据流

这套平台的数据流是：

```text
Gateway / OrderService / InventoryService
  -> OpenTelemetry Collector
     -> Tempo        (看 Trace)
     -> Loki         (看 Logs)
     -> Prometheus   (看 Metrics)
        -> Grafana   (统一展示)
```

这里最重要的设计原则是：

- 业务服务只接 OpenTelemetry
- 业务服务不直接写 Grafana / Loki / Tempo / Prometheus
- Collector 负责把不同信号分发到不同后端

这样做的好处是：

- 业务代码更干净
- 更容易解释每个组件的角色
- 以后要替换平台后端时，业务层改动更小

## 3. 每个文件的作用

### `.env`

作用：

- 集中定义“对宿主机暴露什么端口”
- 避免把端口号硬编码在 Compose 里
- 便于以后换端口时只改一处

你应该重点关注：

- `GRAFANA_PORT`
- `PROMETHEUS_PORT`
- `OTEL_COLLECTOR_GRPC_PORT`
- `OTEL_COLLECTOR_PROM_PORT`
- `OTEL_COLLECTOR_SELF_METRICS_PORT`

### `docker-compose.yml`

作用：

- 定义整套观测平台有哪些容器
- 定义这些容器之间如何连接
- 定义哪些端口需要暴露给宿主机
- 定义哪些配置文件挂进容器

普通用户要记住：

- 这是“启动清单”
- 它不负责定义 Collector 的 pipeline 细节
- 它不负责定义 Prometheus 抓哪些指标
- 它只负责把整套平台实例拉起来

### `otel-collector/otel-collector-config.yml`

作用：

- 定义 Collector 收什么
- 怎样批处理
- 最后发去哪里

它是整套平台里最关键的路由配置。

### `prometheus/prometheus.yml`

作用：

- 定义 Prometheus 去哪里抓指标

它解决的问题是：

- 哪些组件应该被抓
- 抓取周期多长
- 指标入口地址是什么

### `loki/loki-config.yml`

作用：

- 定义 Loki 怎么收日志、怎么存日志

这份配置里最关键的是：

- 允许结构化元数据保留

因为没有它，很多 `TraceId / SpanId / service.name` 元数据会不好用。

### `tempo/tempo-config.yml`

作用：

- 定义 Tempo 怎么收和存 Trace

普通用户要知道：

- Tempo 是“Trace 仓库”
- Grafana 查 Trace 时，本质上是在问 Tempo

### `grafana/provisioning/*`

作用：

- 让 Grafana 启动时自动拥有：
  - Prometheus 数据源
  - Loki 数据源
  - Tempo 数据源
  - 预置仪表盘

这意味着：

- 你不需要手工点很多 UI 才能开始用
- 打开 Grafana 后，基础配置已经就绪

## 4. 参数怎么理解

### 端口类参数

端口类参数回答的是：

> 这个服务对谁开放、从哪里访问它

例如：

- `33000 -> Grafana`
- `9090 -> Prometheus`
- `14318 -> OTel Collector gRPC`

### volume / 挂载类参数

回答的是：

> 容器里的配置文件，实际从宿主机哪个文件读

这意味着你改本地 `yml/json`，容器重启后就会吃到新配置。

### depends_on

回答的是：

> 哪些服务应该先于当前服务启动

它不是严格的“应用级健康检查”，只是启动顺序提示。

### network

回答的是：

> 这些容器是否在同一个内部网络里

如果在同一个 network 里，容器间就可以通过名字互相访问，例如：

- `http://prometheus:9090`
- `http://loki:3100`
- `http://tempo:3200`

## 5. 普通用户最关心的三个问题

### 问题一：为什么要有 Collector

因为没有 Collector：

- 每个业务服务都要知道 Trace 发哪里
- Log 发哪里
- Metric 发哪里

这样业务代码会和具体平台强绑定。

有了 Collector：

- 业务只发 OTLP
- Collector 统一转发

### 问题二：为什么要拆 Prometheus / Loki / Tempo

因为三类数据本来就不是同一种数据：

- Metric：适合时序聚合
- Log：适合逐条文本与结构化排查
- Trace：适合看一次请求跨服务链路

Grafana 只是把这三种数据“摆在一起看”，并不是它自己存三种数据。

### 问题三：为什么还要写这么多文档

因为普通用户打开 `.yml` 时最容易卡在这些问题：

- 这个参数是给谁看的
- 改这个会影响什么
- 为什么这里是 14318，不是 4318
- 哪个组件在收，哪个组件在查

所以这份文档的目标不是重复配置内容，而是把“配置背后的意图”说出来。

## 6. 推荐阅读顺序

- `[otel-collector/README.md](E:/wfcodes/observability-trace-demo/infra/observability/otel-collector/README.md)`
- `[prometheus/README.md](E:/wfcodes/observability-trace-demo/infra/observability/prometheus/README.md)`
- `[loki/README.md](E:/wfcodes/observability-trace-demo/infra/observability/loki/README.md)`
- `[tempo/README.md](E:/wfcodes/observability-trace-demo/infra/observability/tempo/README.md)`
- `[grafana/README.md](E:/wfcodes/observability-trace-demo/infra/observability/grafana/README.md)`
