# Trace 讲解手册

## 1. 成功链路应该长什么样

一次成功下单，理想 Trace 结构大致如下：

```text
gateway.route
└─ HTTP POST /api/orders
   └─ order.create
      ├─ HTTP GET inventoryservice /api/inventory/{sku}
      │  └─ inventory.lookup
      │     ├─ inventory.cache.read
      │     ├─ db query (EF Core / Npgsql)
      │     └─ inventory.cache.populate
      └─ db query (EF Core / Npgsql)
```

## 2. 首次请求

先调用 `POST /api/inventory/seed`。这个接口会写入 PostgreSQL，并清掉对应 Redis 缓存。

随后第一次查询同一 SKU 时就是冷缓存：

- `inventory.cache.read`
  - `cache.hit = false`
- 紧接着会看到数据库 span
- 然后出现 `inventory.cache.populate`

这说明链路是：

`Redis miss -> PostgreSQL -> Redis set`

## 3. 第二次请求

第二次查同一 SKU 时：

- `inventory.cache.read`
  - `cache.hit = true`
- 不再出现数据库回源 span

这说明缓存已经生效。

## 4. 失败链路

库存不足时，重点看：

- `order.create` 是否被标记为 error
- `error.type` 是否是 `business.insufficient_inventory`
- OrderService 日志里是否有对应失败记录

## 5. 怎么在 Tempo 里查

进入：

```text
Explore -> Tempo
```

建议先按 `service.name` 搜：

- `gateway`
- `orderservice`
- `inventoryservice`

## 6. 看 Trace 时要回答的问题

### 问题一：慢在哪层

- Gateway 慢？
- OrderService 慢？
- InventoryService 慢？
- Redis 慢？
- PostgreSQL 慢？

### 问题二：是不是跨服务导致

看 `HttpClient` span 是否显著拉长。

### 问题三：是不是缓存失效

看 `inventory.cache.read` 的 `cache.hit`。

### 问题四：是不是数据库回源拖慢

看 InventoryService 里是否出现数据库 span，以及持续时间是否异常。

如果看不到数据库 span，先确认：

1. 是否刚执行过 `POST /api/inventory/seed`
2. 是否查询的是 `sku-1` / `sku-2` / `sku-3`
3. 是否已经第二次查询同一个 SKU。第二次通常会命中 Redis，不再访问 PostgreSQL
4. 是否在 Tempo 里展开了 client/internal span，而不只是看入口 server span

## 7. Trace 与日志怎么一起看

推荐顺序：

1. 先在 Tempo 找到慢或失败的 Trace
2. 再从 span 跳到 Loki 看日志
3. 用日志里的 `TraceId` 反查同一条链路

这样排障速度会比只翻日志快很多。
