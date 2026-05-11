using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string DefaultNamespace = "observability-trace-demo";
    private const string DefaultVersion = "1.0.0";
    private const string DefaultEnvironment = "Development";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /*--------------------------------------------------------------------------
         * 这一层是整个 solution 的“观测底座”。
         * 目标不是简单把 OpenTelemetry 打开，而是统一每个服务的身份、日志关联、
         * Trace / Metric 自动采集、OTLP 导出和健康检查入口。
         *
         * 这样做的好处：
         * 1. Gateway、OrderService、InventoryService 用同一套资源标签。
         * 2. 任何服务默认都能把 TraceId / SpanId 带进日志。
         * 3. 后续你在 Grafana 里按 service.name / service.namespace 过滤时，
         *    不会出现标签命名风格不一致的问题。
         *------------------------------------------------------------------------*/
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // 给所有 HttpClient 默认挂上基础弹性策略，避免演示时偶发瞬时错误太脆弱。
            http.AddStandardResilienceHandler();

            // 这里开启 Aspire 的服务发现，让 https+http://inventoryservice 这类地址可解析。
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var resourceOptions = LoadResourceOptions(builder.Configuration, builder.Environment);

        // 让普通 ILogger 也自动带上 TraceId / SpanId / ParentId。
        builder.Services.Configure<LoggerFactoryOptions>(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.Tags |
                ActivityTrackingOptions.Baggage;
        });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            /*----------------------------------------------------------------------
             * 这些开关决定了日志进入 Loki 之前，保留多少“结构信息”。
             *
             * IncludeFormattedMessage:
             *   保留格式化后的消息，便于在 Grafana Explore 直接读日志正文。
             * IncludeScopes:
             *   保留 BeginScope() 上下文，适合挂业务维度，例如 order.id / sku。
             * ParseStateValues:
             *   把结构化日志参数展开成字段，后续在 Loki 中更容易过滤。
             *------------------------------------------------------------------------*/
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            logging.SetResourceBuilder(BuildResourceBuilder(resourceOptions));
            ConfigureOtlpLoggingExporter(builder.Configuration, logging);
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => ApplyResource(resource, resourceOptions))
            .WithMetrics(metrics =>
            {
                /*------------------------------------------------------------------
                 * 指标默认“尽量全开”，但仍然遵守一个原则：
                 * 开启高价值、低风险、低基数的内建指标；不把 trace_id 这类字段硬塞进 label。
                 *
                 * 你在 Grafana / Prometheus 里主要能看到：
                 * - ASP.NET Core 请求量、耗时、状态码
                 * - HttpClient 出站请求量、耗时
                 * - Runtime 指标：GC、线程池、内存、异常
                 * - EF Core / Redis 如果调用方项目显式注册对应 instrumentation
                 *------------------------------------------------------------------*/
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                /*------------------------------------------------------------------
                 * Trace 层默认采集三类自动来源：
                 * 1. AspNetCore 入站请求
                 * 2. HttpClient 出站请求
                 * 3. 业务方显式 AddSource(...) 注册的自定义 ActivitySource
                 *
                 * 健康检查接口会被过滤掉，避免它们污染面板上的吞吐与耗时图。
                 *------------------------------------------------------------------*/
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    });
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    public static TBuilder AddProjectOpenTelemetry<TBuilder>(
        this TBuilder builder,
        IReadOnlyCollection<string> activitySources,
        IReadOnlyCollection<string> meters,
        bool includeEntityFramework = false) where TBuilder : IHostApplicationBuilder
    {
        /*--------------------------------------------------------------------------
         * 这里用于“每个业务项目自己的补充观测配置”。
         *
         * ServiceDefaults 只负责通用的 AspNetCore / HttpClient / Runtime；
         * 业务项目再告诉它：
         * - 我有哪些自定义 ActivitySource
         * - 我有哪些自定义 Meter
         * - 我是否需要 EF Core 自动采集
         *
         * 这样既能共享底座，又不会把 Gateway / Order / Inventory 的差异揉烂。
         *------------------------------------------------------------------------*/
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                foreach (var activitySource in activitySources)
                {
                    tracing.AddSource(activitySource);
                }

                if (includeEntityFramework)
                {
                    tracing.AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        // 演示项目里保留 SQL 文本有助于教学；生产环境应再评估脱敏策略。
                        options.EnrichWithIDbCommand = static (activity, command) =>
                        {
                            activity.SetTag("db.statement", command.CommandText);
                        };
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                foreach (var meter in meters)
                {
                    metrics.AddMeter(meter);
                }
            });

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /*--------------------------------------------------------------------------
         * 统一读取 OTLP endpoint。
         * 这里优先走环境变量 OTEL_EXPORTER_OTLP_ENDPOINT，因为：
         * 1. Aspire AppHost 下发环境变量最自然；
         * 2. 本地 docker compose 的 Collector 对外暴露端口就是 localhost:14318；
         * 3. 后续切换到其他 collector 或云端网关时，不用改业务代码。
         *------------------------------------------------------------------------*/
        var endpoint = ResolveOtlpEndpoint(builder.Configuration);
        var useOtlpExporter = !string.IsNullOrWhiteSpace(endpoint);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(endpoint!);
                    });
                })
                .WithTracing(tracing =>
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(endpoint!);
                    });
                });
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);

            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    private static ResourceOptions LoadResourceOptions(IConfiguration configuration, IHostEnvironment environment)
    {
        return new ResourceOptions(
            configuration["OpenTelemetry:ServiceName"] ?? environment.ApplicationName,
            configuration["OpenTelemetry:ServiceNamespace"] ?? DefaultNamespace,
            configuration["OpenTelemetry:ServiceVersion"] ?? DefaultVersion,
            configuration["OpenTelemetry:DeploymentEnvironment"] ?? environment.EnvironmentName ?? DefaultEnvironment);
    }

    private static ResourceBuilder BuildResourceBuilder(ResourceOptions options)
    {
        return ResourceBuilder.CreateDefault().AddService(
            serviceName: options.ServiceName,
            serviceNamespace: options.ServiceNamespace,
            serviceVersion: options.ServiceVersion,
            serviceInstanceId: Environment.MachineName);
    }

    private static void ApplyResource(ResourceBuilder builder, ResourceOptions options)
    {
        builder
            .AddService(
                serviceName: options.ServiceName,
                serviceNamespace: options.ServiceNamespace,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment", options.DeploymentEnvironment),
                new KeyValuePair<string, object>("host.name", Environment.MachineName)
            ]);
    }

    private static void ConfigureOtlpLoggingExporter(IConfiguration configuration, OpenTelemetryLoggerOptions logging)
    {
        var endpoint = ResolveOtlpEndpoint(configuration);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        logging.AddOtlpExporter(exporter =>
        {
            exporter.Endpoint = new Uri(endpoint);
        });
    }

    private static string? ResolveOtlpEndpoint(IConfiguration configuration)
    {
        return configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? configuration["OpenTelemetry:OtlpEndpoint"];
    }

    private sealed record ResourceOptions(
        string ServiceName,
        string ServiceNamespace,
        string ServiceVersion,
        string DeploymentEnvironment);
}
