# Prometheus 配置说明

文件：

- `[prometheus.yml](E:/wfcodes/observability-trace-demo/infra/observability/prometheus/prometheus.yml)`

## 1. 它的作用

Prometheus 是指标数据库。

它负责：

- 周期性去抓指标
- 按时间序列存下来
- 给 Grafana 查询

## 2. 配置结构

### `global`

定义全局抓取策略。

关键参数：

- `scrape_interval: 15s`
  - 每 15 秒抓一次
- `evaluation_interval: 15s`
  - 规则计算周期 15 秒

### `scrape_configs`

定义到底抓谁。

## 3. 当前抓取任务解释

### `collector-app-metrics`

目标：

- `otel-collector:9464`

作用：

- 抓业务服务经 Collector 汇聚后的应用指标

这是你最关心的一组业务 metric 入口。

### `collector-self`

目标：

- `otel-collector:8888`

作用：

- 抓 Collector 自己的内部健康与吞吐指标

### `prometheus`

作用：

- 让 Prometheus 也能暴露自己的健康情况

### `grafana`

作用：

- 抓 Grafana 自己的指标

前提：

- `GF_METRICS_ENABLED=true`

### `loki`

作用：

- 抓 Loki 自己的内部指标

### `tempo`

作用：

- 抓 Tempo 自己的内部指标

## 4. 普通用户要理解的核心点

Prometheus 不知道“业务代码”是什么。

它只知道：

- 哪个地址暴露了指标
- 多久抓一次

所以 `prometheus.yml` 的本质是：

> 指标抓取清单

不是业务配置。
