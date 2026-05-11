using Microsoft.EntityFrameworkCore;

namespace ObservabilityTraceDemo.OrderService.Infrastructure;

public sealed class OrderSchemaInitializer(OrderDbContext dbContext, ILogger<OrderSchemaInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        /*--------------------------------------------------------------------------
         * 这里不用 Migration，而是用显式 SQL 建表，原因有两个：
         * 1. 演示仓库希望开箱即跑，不把首次学习门槛放在迁移命令上。
         * 2. 当前是一个数据库、两个 schema、两个独立服务，各自负责自己的表。
         *
         * 生产项目通常建议走正式迁移；这里为了教学可读性，选择更直白的方式。
         *------------------------------------------------------------------------*/
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS ordering;

            CREATE TABLE IF NOT EXISTS ordering.orders
            (
                order_id uuid PRIMARY KEY,
                sku varchar(128) NOT NULL,
                quantity integer NOT NULL,
                customer_id varchar(128) NOT NULL,
                created_at_utc timestamptz NOT NULL
            );
            """,
            cancellationToken);

        logger.LogInformation("订单 schema 初始化完成。Schema={Schema}, Table={Table}", "ordering", "orders");
    }
}
