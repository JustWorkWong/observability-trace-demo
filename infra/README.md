# Infra 目录说明

`infra/` 目录放的是“业务代码之外，但业务运行必须依赖的基础设施配置”。

在这个仓库里，`infra` 的职责很单纯：

- 不放业务接口代码
- 不放领域模型
- 不放测试
- 只放本地演示所需的平台组件配置

当前只有一个子目录：

```text
infra/
└─ observability/
```

## 1. 为什么要单独有 `infra/`

普通用户最容易混淆的一点是：

- `src/` 里的项目回答“业务做什么”
- `infra/` 里的配置回答“这些业务跑起来依赖什么平台”

如果把这些基础设施配置直接塞进 `src/`，阅读体验会很差：

- 业务代码和平台配置混在一起
- 不容易看出哪些是应用逻辑，哪些是可观测性平台
- 面试或演示时也不方便讲清楚边界

所以这里明确分层：

- `src/` 管应用
- `infra/` 管平台

## 2. `observability/` 里有什么

`infra/observability/` 是整套观测平台目录，核心包括：

- `docker-compose.yml`
  - 定义 Grafana / Prometheus / Loki / Tempo / OTel Collector 这几个容器怎么一起启动
- `.env`
  - 集中定义本地端口和少量环境变量
- `otel-collector/`
  - Collector 的接收、处理、导出规则
- `prometheus/`
  - Prometheus 抓取规则
- `loki/`
  - Loki 日志存储规则
- `tempo/`
  - Tempo Trace 存储规则
- `grafana/`
  - Grafana 的数据源和仪表盘自动装配

## 3. 普通用户应该先看什么

如果你第一次接触这套目录，建议按这个顺序读：

1. `[infra/observability/README.md](E:/wfcodes/observability-trace-demo/infra/observability/README.md)`
2. `[docker-compose.yml](E:/wfcodes/observability-trace-demo/infra/observability/docker-compose.yml)`
3. `[.env](E:/wfcodes/observability-trace-demo/infra/observability/.env)`
4. `otel-collector/README.md`
5. `prometheus/README.md`
6. `loki/README.md`
7. `tempo/README.md`
8. `grafana/README.md`

## 4. 这一层解决什么问题

它解决的是：

- 观测平台怎么本地一键启动
- 应用发出来的 Trace / Metric / Log 往哪里走
- Grafana 为什么能同时看到三类数据
- Prometheus / Loki / Tempo 各自分别负责什么

一句话：

> `infra/` 让“平台为什么存在、怎么启动、怎么连接”这件事变得可见。
