# 生产部署配置指南

这份文档回答四个问题：

- 生产上整体应该怎么配置
- 每个业务服务应该怎么配置
- Docker Compose 应该怎么配置
- Kubernetes 应该怎么配置，以及多实例如何自动发现

这不是一份可以直接复制到任何公司的最终模板。生产环境一定会受网络、权限、镜像仓库、证书、日志保留和合规要求影响。这里给的是本仓库这套架构的标准落地方式和关键参数解释。

## 1. 生产拓扑

推荐的生产数据流是：

```text
Client
  -> Ingress / Load Balancer
     -> Gateway
        -> OrderService
           -> InventoryService
              -> Redis
              -> PostgreSQL

Gateway / OrderService / InventoryService
  -> OTLP gRPC
     -> OpenTelemetry Collector
        -> Tempo      (Trace)
        -> Loki       (Logs)
        -> Prometheus (Metrics)
           -> Grafana
```

核心原则：

- 业务服务只知道 `OTLP endpoint`，不要直接耦合 Grafana / Tempo / Loki / Prometheus。
- `service.name` 固定表示服务名，例如 `gateway`、`orderservice`、`inventoryservice`。
- `service.namespace` 固定表示系统名，例如 `observability-trace-demo`。
- `service.instance.id` 用来区分同一个服务的多个实例。本仓库当前由运行环境主机名生成，在 Docker / K8s 中通常对应容器名或 Pod 名。
- Prometheus 里的 `job` 是抓取任务，不是业务服务名。按服务过滤要用 `service_name`。

## 2. 服务侧配置

### 2.1 每个服务都应该配置的环境变量

生产上不要只依赖 `appsettings.json`。建议用环境变量覆盖关键配置。

```yaml
environment:
  # 让 ASP.NET Core 在容器内监听固定端口。
  # K8s Service / Docker Compose service 都会转发到这个端口。
  ASPNETCORE_HTTP_PORTS: "8080"

  # 生产环境标识。
  # 它会进入 OpenTelemetry Resource，最后在 Prometheus / Loki / Tempo 中变成 deployment_environment。
  ASPNETCORE_ENVIRONMENT: "Production"

  # 业务服务名。
  # 生产中必须固定，不能带 Pod 名、容器 ID、随机后缀，否则 Grafana 无法稳定按服务聚合。
  OpenTelemetry__ServiceName: "orderservice"

  # 系统或业务域名称。
  # 多个系统共用一套观测平台时，用它把服务归组。
  OpenTelemetry__ServiceNamespace: "observability-trace-demo"

  # 服务版本。
  # 推荐写镜像 tag、Git SHA 或发布版本号，便于排查“是不是某个版本变慢”。
  OpenTelemetry__ServiceVersion: "1.0.0"

  # 部署环境。
  # 建议使用 Production / Staging / Development 这类稳定枚举。
  OpenTelemetry__DeploymentEnvironment: "Production"

  # OTLP gRPC endpoint。
  # 在 K8s 中通常指向 Collector Service。
  # 在 Docker Compose 中通常指向 collector 容器名。
  OpenTelemetry__CollectorOtlpEndpoint: "http://otel-collector:4317"
```

如果服务由 Aspire AppHost 本地启动，也会存在 `OTEL_EXPORTER_OTLP_ENDPOINT`。生产部署通常不依赖 AppHost，而是显式设置 `OpenTelemetry__CollectorOtlpEndpoint` 或 `OpenTelemetry__OtlpEndpoint`。

### 2.2 Gateway 配置

Gateway 的生产职责：

- 对外暴露统一入口
- 转发 `/api/orders/*` 到 OrderService
- 转发 `/api/inventory/*` 到 InventoryService
- 输出入口请求、转发耗时、转发失败等指标

关键配置：

```yaml
environment:
  OpenTelemetry__ServiceName: "gateway"

  # 生产建议把 YARP 目标地址配置成平台服务名。
  # Docker Compose: http://orderservice:8080
  # Kubernetes: http://orderservice.default.svc.cluster.local:8080
```

当前仓库的 Gateway 路由在 `src/ObservabilityTraceDemo.Gateway/appsettings.json`。生产上可以继续用配置文件，也可以用环境变量覆盖 YARP 配置。原则是：

- 网关只依赖服务名，不依赖单个实例 IP。
- 网关只暴露公网需要的 API，不把 `/health`、`/alive` 通过公网路由暴露出去。

### 2.3 OrderService 配置

OrderService 的生产职责：

- 接收下单请求
- 调用 InventoryService
- 写 PostgreSQL 的 `ordering` schema
- 输出订单成功、失败、耗时指标

关键配置：

```yaml
environment:
  OpenTelemetry__ServiceName: "orderservice"

  # 下游库存服务地址。
  # Docker Compose 推荐使用服务 DNS。
  DownstreamServices__InventoryServiceBaseAddress: "http://inventoryservice:8080"

  # PostgreSQL 连接串。
  # 生产中应来自 Secret，不要写死在镜像或 Git。
  ConnectionStrings__observabilitydb: "Host=postgresql;Port=5432;Database=observabilitydb;Username=app_order;Password=${ORDER_DB_PASSWORD};Pooling=true;Maximum Pool Size=100"
```

生产注意点：

- 数据库账号建议按服务拆分，OrderService 只授权 `ordering` schema。
- 连接池上限要和 PostgreSQL `max_connections`、实例数一起算。
- `db.statement` 生产上可能包含敏感信息，应评估是否关闭 SQL 文本采集或做脱敏。

### 2.4 InventoryService 配置

InventoryService 的生产职责：

- 查询库存
- 优先读 Redis
- Redis 未命中时回源 PostgreSQL 的 `inventory` schema
- 回填 Redis

关键配置：

```yaml
environment:
  OpenTelemetry__ServiceName: "inventoryservice"

  # PostgreSQL 连接串。
  # 生产中应来自 Secret。
  ConnectionStrings__observabilitydb: "Host=postgresql;Port=5432;Database=observabilitydb;Username=app_inventory;Password=${INVENTORY_DB_PASSWORD};Pooling=true;Maximum Pool Size=100"

  # Redis 连接串。
  # 如果使用云 Redis 或 Redis Cluster，应按供应商要求加入 ssl、abortConnect、connectRetry 等参数。
  ConnectionStrings__redis: "redis:6379,abortConnect=false,connectRetry=3"
```

生产注意点：

- Redis key 当前约定为 `inventory:sku:{sku}`。
- 不要把高基数的 `sku` 放到指标 label 中。本仓库已经避免在核心自定义指标里使用高基数字段。
- Redis 慢不一定是 Redis 本身问题，也可能是网络、连接数、序列化或线程池排队。

## 3. Docker Compose 生产化配置

Docker Compose 更适合单机演示、开发联调、小规模内网部署。真正高可用生产更建议 Kubernetes。

如果必须用 Compose，至少遵守这些规则：

- 不给可横向扩展的业务服务写 `container_name`。
- 只有 Gateway 暴露宿主机端口，OrderService / InventoryService 只在内部网络访问。
- 连接串和密码走 `.env` 或 Docker secrets，不写进镜像。
- 服务之间用 Compose service name 访问，不写容器 IP。
- 多实例用 `docker compose up --scale inventoryservice=3 --scale orderservice=2`。

参考结构：

```yaml
services:
  gateway:
    image: registry.example.com/observability-demo/gateway:1.0.0
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_HTTP_PORTS: "8080"
      ASPNETCORE_ENVIRONMENT: "Production"
      OpenTelemetry__ServiceName: "gateway"
      OpenTelemetry__ServiceNamespace: "observability-trace-demo"
      OpenTelemetry__ServiceVersion: "1.0.0"
      OpenTelemetry__DeploymentEnvironment: "Production"
      OpenTelemetry__CollectorOtlpEndpoint: "http://otel-collector:4317"
    depends_on:
      - otel-collector
      - orderservice
      - inventoryservice
    networks:
      - app

  orderservice:
    image: registry.example.com/observability-demo/orderservice:1.0.0
    environment:
      ASPNETCORE_HTTP_PORTS: "8080"
      ASPNETCORE_ENVIRONMENT: "Production"
      OpenTelemetry__ServiceName: "orderservice"
      OpenTelemetry__ServiceNamespace: "observability-trace-demo"
      OpenTelemetry__ServiceVersion: "1.0.0"
      OpenTelemetry__DeploymentEnvironment: "Production"
      OpenTelemetry__CollectorOtlpEndpoint: "http://otel-collector:4317"
      DownstreamServices__InventoryServiceBaseAddress: "http://inventoryservice:8080"
      ConnectionStrings__observabilitydb: "${ORDER_DB_CONNECTION}"
    expose:
      - "8080"
    depends_on:
      - postgresql
      - inventoryservice
      - otel-collector
    networks:
      - app

  inventoryservice:
    image: registry.example.com/observability-demo/inventoryservice:1.0.0
    environment:
      ASPNETCORE_HTTP_PORTS: "8080"
      ASPNETCORE_ENVIRONMENT: "Production"
      OpenTelemetry__ServiceName: "inventoryservice"
      OpenTelemetry__ServiceNamespace: "observability-trace-demo"
      OpenTelemetry__ServiceVersion: "1.0.0"
      OpenTelemetry__DeploymentEnvironment: "Production"
      OpenTelemetry__CollectorOtlpEndpoint: "http://otel-collector:4317"
      ConnectionStrings__observabilitydb: "${INVENTORY_DB_CONNECTION}"
      ConnectionStrings__redis: "${REDIS_CONNECTION}"
    expose:
      - "8080"
    depends_on:
      - postgresql
      - redis
      - otel-collector
    networks:
      - app

  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.122.1
    command:
      - "--config=/etc/otelcol-contrib/otel-collector-config.yml"
    volumes:
      - ./otel-collector/otel-collector-config.yml:/etc/otelcol-contrib/otel-collector-config.yml:ro
    expose:
      - "4317"
      - "9464"
      - "8888"
    networks:
      - app
      - observability

networks:
  app:
  observability:
```

Compose 多实例发现方式：

- `orderservice` 访问 `http://inventoryservice:8080`。
- Docker 内置 DNS 会解析服务名。
- 不要给 `inventoryservice` 固定 `container_name`，否则多个实例会冲突。
- 不要给每个业务实例都暴露宿主机端口，横向扩展时端口会冲突。

Compose 的限制：

- Compose service name 的负载均衡能力有限，不等同于 Kubernetes Service。
- 服务滚动升级、自动恢复、HPA、Pod 级发现都不是 Compose 的强项。
- 生产高可用场景应优先使用 Kubernetes 或托管平台。

## 4. Kubernetes 生产配置

### 4.1 Namespace 建议

建议拆两个 namespace：

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: observability
---
apiVersion: v1
kind: Namespace
metadata:
  name: observability-demo
```

- `observability` 放 Collector、Prometheus、Grafana、Loki、Tempo。
- `observability-demo` 放 Gateway、OrderService、InventoryService。

### 4.2 Collector Service

业务服务通过 K8s DNS 自动发现 Collector：

```yaml
apiVersion: v1
kind: Service
metadata:
  name: otel-collector
  namespace: observability
spec:
  selector:
    app: otel-collector
  ports:
    - name: otlp-grpc
      port: 4317
      targetPort: 4317
    - name: metrics
      port: 9464
      targetPort: 9464
    - name: self-metrics
      port: 8888
      targetPort: 8888
```

业务服务里的 endpoint 写：

```text
http://otel-collector.observability.svc.cluster.local:4317
```

### 4.3 Collector Deployment

第一版生产化建议先用 Gateway Collector 模式：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: otel-collector
  namespace: observability
spec:
  replicas: 2
  selector:
    matchLabels:
      app: otel-collector
  template:
    metadata:
      labels:
        app: otel-collector
    spec:
      containers:
        - name: otel-collector
          image: otel/opentelemetry-collector-contrib:0.122.1
          args:
            - "--config=/etc/otelcol-contrib/otel-collector-config.yml"
          ports:
            - containerPort: 4317
              name: otlp-grpc
            - containerPort: 9464
              name: metrics
            - containerPort: 8888
              name: self-metrics
          volumeMounts:
            - name: config
              mountPath: /etc/otelcol-contrib
      volumes:
        - name: config
          configMap:
            name: otel-collector-config
```

说明：

- `replicas: 2` 让 Collector 本身具备基础冗余。
- 业务服务通过 `otel-collector` Service 发 OTLP，K8s 自动在 Collector Pod 间负载均衡。
- 更大规模时可以升级为 `DaemonSet agent + Deployment gateway` 两层 Collector。

### 4.4 业务服务 Deployment

以 OrderService 为例：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: orderservice
  namespace: observability-demo
spec:
  replicas: 3
  selector:
    matchLabels:
      app: orderservice
  template:
    metadata:
      labels:
        app: orderservice
    spec:
      containers:
        - name: orderservice
          image: registry.example.com/observability-demo/orderservice:1.0.0
          ports:
            - containerPort: 8080
              name: http
          env:
            - name: ASPNETCORE_HTTP_PORTS
              value: "8080"
            - name: ASPNETCORE_ENVIRONMENT
              value: "Production"
            - name: OpenTelemetry__ServiceName
              value: "orderservice"
            - name: OpenTelemetry__ServiceNamespace
              value: "observability-trace-demo"
            - name: OpenTelemetry__ServiceVersion
              value: "1.0.0"
            - name: OpenTelemetry__DeploymentEnvironment
              value: "Production"
            - name: OpenTelemetry__CollectorOtlpEndpoint
              value: "http://otel-collector.observability.svc.cluster.local:4317"
            - name: DownstreamServices__InventoryServiceBaseAddress
              value: "http://inventoryservice.observability-demo.svc.cluster.local:8080"
            - name: ConnectionStrings__observabilitydb
              valueFrom:
                secretKeyRef:
                  name: orderservice-secret
                  key: observabilitydb
          readinessProbe:
            httpGet:
              path: /health
              port: http
            initialDelaySeconds: 10
            periodSeconds: 10
          livenessProbe:
            httpGet:
              path: /alive
              port: http
            initialDelaySeconds: 20
            periodSeconds: 20
          resources:
            requests:
              cpu: 100m
              memory: 128Mi
            limits:
              cpu: 500m
              memory: 512Mi
```

InventoryService 类似，只是多一个 Redis 连接串：

```yaml
- name: ConnectionStrings__redis
  valueFrom:
    secretKeyRef:
      name: inventoryservice-secret
      key: redis
```

### 4.5 业务服务 Service

多实例自动发现依赖 K8s Service：

```yaml
apiVersion: v1
kind: Service
metadata:
  name: inventoryservice
  namespace: observability-demo
spec:
  selector:
    app: inventoryservice
  ports:
    - name: http
      port: 8080
      targetPort: http
```

OrderService 只需要访问：

```text
http://inventoryservice.observability-demo.svc.cluster.local:8080
```

K8s 会自动把流量负载均衡到所有带 `app=inventoryservice` 的 Pod。

### 4.6 Gateway 暴露入口

生产建议：

- Gateway 通过 `Service` 暴露集群内入口。
- 对公网用 `Ingress`、云厂商 LoadBalancer 或 API Gateway。
- OrderService / InventoryService 不直接暴露公网。

简化示例：

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: observability-demo-gateway
  namespace: observability-demo
spec:
  rules:
    - host: demo.example.com
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: gateway
                port:
                  number: 8080
```

## 5. 多实例如何自动发现

### 5.1 业务调用自动发现

Docker Compose：

- 服务名就是 DNS 名。
- `orderservice` 调用 `http://inventoryservice:8080`。
- 扩容时不要使用 `container_name`。

Kubernetes：

- `Service` 是稳定入口。
- Pod 可以随时新增、删除、重建。
- 调用方只访问 Service DNS，不访问 Pod IP。

### 5.2 Telemetry 自动发现

本仓库采用 OTLP 推送到 Collector：

- 业务实例启动后主动把 Trace / Metric / Log 发给 Collector。
- Prometheus 不需要逐个发现业务 Pod。
- Prometheus 只需要抓 Collector 的 `9464` 指标出口。

这也是为什么 Grafana 里要按 `service_name` 看服务，而不是按 `job` 看服务：

- `job=collector-app-metrics` 表示 Prometheus 抓的是 Collector。
- `service_name=orderservice` 才表示业务服务。

### 5.3 多实例在 Grafana 里怎么区分

常用维度：

- `service_name`：服务名，用于聚合。
- `service_namespace`：系统名，用于隔离系统。
- `service_instance_id`：实例名，用于定位某个 Pod / 容器。
- `host_name`：宿主机或容器主机名。
- `deployment_environment`：环境名。

推荐查看顺序：

1. 先按 `service_name` 看整体错误率和 P95 / P99。
2. 再按 `service_instance_id` 判断是不是某个实例异常。
3. 最后用 TraceId 到 Tempo / Loki 查单次请求。

PromQL 示例：

```promql
sum(rate(http_server_request_duration_seconds_count{service_namespace="observability-trace-demo"}[5m])) by (service_name)
```

```promql
sum(rate(http_server_request_duration_seconds_count{service_name="inventoryservice"}[5m])) by (service_instance_id)
```

```promql
histogram_quantile(
  0.99,
  sum(rate(http_server_request_duration_seconds_bucket{service_name="orderservice"}[5m])) by (le, service_instance_id)
)
```

### 5.4 Prometheus 发现 Collector

如果使用 Prometheus Operator，建议用 `ServiceMonitor`：

```yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata:
  name: otel-collector
  namespace: observability
spec:
  selector:
    matchLabels:
      app: otel-collector
  endpoints:
    - port: metrics
      interval: 15s
    - port: self-metrics
      interval: 15s
```

如果不用 Prometheus Operator，可以在 Prometheus 配置里使用 Kubernetes service discovery。核心思想是让 Prometheus 自动发现带特定 label / annotation 的 Service，而不是写死 Pod IP。

## 6. 生产配置重点

### 6.1 采样

本地 demo 默认尽量全开，生产不能无脑全量 Trace。

建议策略：

- 正常请求按比例采样。
- 错误请求尽量保留。
- 高价值业务链路提高采样率。
- 大流量低价值健康检查不进 Trace。

### 6.2 保留时间

建议从业务需要倒推：

- Prometheus：核心指标通常 15 到 30 天，长期趋势进远端存储。
- Loki：按日志量和合规要求设置，常见 7 到 30 天。
- Tempo：Trace 成本较高，常见 3 到 14 天。

### 6.3 认证与网络

生产必须处理：

- Grafana 登录认证和权限。
- Prometheus / Loki / Tempo 不直接暴露公网。
- Collector OTLP 入口只允许业务 namespace 访问。
- 跨集群或跨网络传输要启用 TLS。

### 6.4 Secret 管理

不要把这些内容写进 Git：

- 数据库密码
- Redis 密码
- Grafana admin 密码
- 云厂商 token
- 外部 OTLP endpoint token

K8s 用 Secret 或外部 Secret 管理系统。Docker Compose 至少用 `.env`，更正式的环境使用 Docker secrets 或平台密钥管理。

### 6.5 资源限制

生产必须给业务服务和 Collector 设置资源请求与限制。

重点关注：

- Collector CPU 是否打满。
- Collector exporter 是否失败。
- 应用内存是否接近 limit。
- 线程池队列是否持续上升。
- DB 连接池是否耗尽。

## 7. 易错点

### 7.1 把 `job` 当成服务名

在这套架构里，Prometheus 抓的是 Collector，所以 `job` 常常是：

```text
collector-app-metrics
```

业务服务名要看：

```text
service_name
```

### 7.2 每个实例都写不同的 `service.name`

错误做法：

```text
orderservice-pod-1
orderservice-pod-2
```

正确做法：

```text
service.name = orderservice
service.instance.id = pod name
```

### 7.3 在指标 label 放高基数字段

不要把这些作为 metric label：

- orderId
- traceId
- userId
- 原始 URL
- 无限增长的 sku

它们会快速放大 Prometheus 时间序列数量。

### 7.4 多实例却固定容器名

Docker Compose 里如果写了：

```yaml
container_name: inventoryservice
```

就很难横向扩容。生产化 Compose 模板里不要给业务服务固定 `container_name`。

### 7.5 只配置 dashboard，不配置告警

生产至少要有：

- 请求错误率告警
- P95 / P99 延迟告警
- Collector 导出失败告警
- Prometheus target down 告警
- DB 连接池接近耗尽告警
- Redis 命中率异常下降告警

## 8. 上线前检查清单

- 服务名是否稳定：`gateway / orderservice / inventoryservice`
- `service.namespace` 是否统一
- `deployment.environment` 是否正确
- 业务服务是否能访问 Collector OTLP gRPC
- Prometheus 是否能抓 Collector `9464`
- Grafana 是否能看到 `service_name` 标签
- `/health` 和 `/alive` 是否只在集群内可访问
- DB / Redis 连接串是否来自 Secret
- 是否设置资源 requests / limits
- 是否配置日志保留、Trace 保留、指标保留
- 是否有基础告警和 runbook
