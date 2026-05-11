# Loki 配置说明

文件：

- `[loki-config.yml](E:/wfcodes/observability-trace-demo/infra/observability/loki/loki-config.yml)`

## 1. 它的作用

Loki 是日志存储与查询后端。

它负责：

- 接收 Collector 发来的日志
- 保存结构化日志和标签
- 给 Grafana Explore 查询

## 2. 为什么这份配置重要

很多人以为日志只要能存就够了，但对可观测性来说，最重要的是：

- 能不能按服务过滤
- 能不能按 `TraceId` 反查
- 能不能保留结构化字段

这份配置就是在保证这些能力。

## 3. 关键配置解释

### `auth_enabled: false`

作用：

- 本地 demo 不启认证

原因：

- 避免把重点放在账号体系上

### `allow_structured_metadata: true`

这是这份文件里最关键的参数之一。

作用：

- 允许保留 OTLP 日志里的结构化元数据

没有它会怎样：

- 很多 `TraceId / SpanId / service.name / attributes` 用起来会差很多

### `schema: v13`

作用：

- 指定 Loki 使用哪一版日志 schema

### `object_store: filesystem`

作用：

- 直接存本地文件系统

这符合 demo 场景，不追求云对象存储。

## 4. 你应该怎么看它

把 Loki 想成：

> “日志数据库”

但它不是关系库，也不是全文搜索引擎那套思路。

它更偏向：

- 标签过滤
- 时间范围过滤
- 日志正文搜索

而不是复杂 SQL。
