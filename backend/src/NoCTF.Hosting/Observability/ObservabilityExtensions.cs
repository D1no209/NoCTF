using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using OpenTelemetry.Exporter;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Infrastructure.Observability;
using NoCTF.Infrastructure.Caching;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoCTF.Hosting.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddNoCtfObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        if (!configuration.GetValue("Observability:Enabled", true))
            return services;
        services.TryAddSingleton(TimeProvider.System);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IFusionCacheProvider)))
            services.AddHostedService<FusionCacheMetricsAgent>();
        if (services.Any(descriptor => descriptor.ServiceType
                == typeof(IDbContextFactory<NoCtfDbContext>)))
            services.AddHostedService<RuntimeWaitingMetricsAgent>();
        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: ThisAssemblyVersion.Value)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment",
                        configuration["DOTNET_ENVIRONMENT"] ?? "Production")
                ]))
            .WithMetrics(metrics => metrics
                .AddMeter(NoCtfTelemetry.MeterName)
                .AddNoCtfDurationViews()
                .AddMeter("Wolverine*")
                .AddMeter("Npgsql")
                .AddMeter("Microsoft.EntityFrameworkCore")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter())
            .WithTracing(tracing => tracing
                .AddSource(NoCtfTelemetry.ActivitySourceName)
                .AddSource("Wolverine")
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/metrics")
                        && !context.Request.Path.StartsWithSegments("/health");
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation(options => options.RecordException = true)
                .AddSource("Npgsql"));

        if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            openTelemetry.WithTracing(tracing => tracing.AddOtlpExporter());

        var lokiBaseUrl = configuration["Observability:LokiBaseUrl"];
        if (string.IsNullOrWhiteSpace(lokiBaseUrl))
        {
            if (configuration.GetValue("Observability:RequireLoki", false))
                throw new InvalidOperationException("Observability:LokiBaseUrl is required.");
            return services;
        }
        if (!Uri.TryCreate(lokiBaseUrl, UriKind.Absolute, out var loki)
            || loki.Scheme != Uri.UriSchemeHttp
            || loki.UserInfo.Length > 0 || loki.Query.Length > 0
            || loki.Fragment.Length > 0 || loki.AbsolutePath != "/")
            throw new InvalidOperationException("Observability:LokiBaseUrl must be a private HTTP origin.");

        var defaultService = serviceName.EndsWith("-api", StringComparison.Ordinal)
            ? PlatformLogService.Api
            : serviceName.EndsWith("-worker", StringComparison.Ordinal)
                ? PlatformLogService.Worker
                : serviceName.EndsWith("-runner", StringComparison.Ordinal)
                    ? PlatformLogService.Runner : PlatformLogService.Host;
        services.AddSingleton<PlatformLogBroadcastQueue>();
        services.TryAddSingleton<PlatformLogUserIdProtector>();
        services.AddHostedService<PlatformLogBroadcastAgent>();
        services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeFormattedMessage = true;
            options.ParseStateValues = true;
            options.IncludeScopes = false;
        });
        services.AddLogging(logging => logging.AddFilter<OpenTelemetryLoggerProvider>(
            (category, level) => category is not null
                && category.StartsWith("NoCTF.", StringComparison.Ordinal)
                && level >= Microsoft.Extensions.Logging.LogLevel.Information));
        openTelemetry.WithLogging(logging => logging
            .AddProcessor(provider => new RedactedPlatformLogProcessor(
                provider.GetRequiredService<PlatformLogBroadcastQueue>(), defaultService,
                provider.GetRequiredService<PlatformLogUserIdProtector>()))
            .AddOtlpExporter((exporter, processor) =>
            {
                exporter.Endpoint = new Uri(loki, "otlp/v1/logs");
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                processor.ExportProcessorType = OpenTelemetry.ExportProcessorType.Batch;
                processor.BatchExportProcessorOptions.MaxQueueSize = 4096;
                processor.BatchExportProcessorOptions.MaxExportBatchSize = 512;
            }));

        return services;
    }

    internal static MeterProviderBuilder AddNoCtfDurationViews(this MeterProviderBuilder metrics)
    {
        // Instrument names, not the names rewritten by the Prometheus exporter. All values are seconds.
        foreach (var name in new[]
        {
            "noctf.api.request.duration",
            "noctf.nats.operation.duration",
            "noctf.signalr.publish.duration", "noctf.runner.claim.duration",
            "noctf.leaderboard.projection.duration", "noctf.scheduler.rebuild.duration",
            "noctf.scheduler.dispatch.lateness",
            "noctf.gameplay_fact.processing.duration",
            "noctf.gameplay_fact.stage.duration",
            "noctf.runtime.dispatch.stage.duration"
        })
        {
            var view = new ExplicitBucketHistogramConfiguration
            {
                Boundaries = [0.0001, 0.00025, 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025,
                    0.05, 0.1, 0.25, 0.5, 0.8, 1, 2.5, 5, 10, 30, 60, 120, 300]
            };
            // Completed SignalR connections and file transfers share this instrument but have
            // separate request_kind labels. Keep a long tail without sacrificing REST precision.
            if (name == "noctf.api.request.duration")
                view.Boundaries = [.. view.Boundaries, 600, 1800, 3600, 21600, 86400];
            metrics.AddView(name, view);
        }
        return metrics;
    }

    public static WebApplication UseNoCtfObservability(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Observability:Enabled", true))
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/metrics")
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                else
                    await next();
            });
            return app;
        }
        var metricsPort = app.Configuration.GetValue("Observability:MetricsPort", 9464);
        if (metricsPort is < 1 or > 65_535)
            throw new InvalidOperationException(
                "Observability:MetricsPort must be a valid TCP port.");

        app.Use(async (context, next) =>
        {
            var metricsPath = context.Request.Path == "/metrics";
            var metricsListener = context.Connection.LocalPort == metricsPort;
            if (metricsPath != metricsListener)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (metricsPath || context.Request.Path.StartsWithSegments("/health"))
            {
                await next();
                return;
            }

            var started = Stopwatch.GetTimestamp();
            var outcome = "success";
            try
            {
                await next();
                outcome = context.Response.StatusCode switch
                {
                    >= 500 => "server_error",
                    >= 400 => "client_error",
                    _ => "success"
                };
            }
            catch
            {
                outcome = "server_error";
                throw;
            }
            finally
            {
                var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                    ?? "unmatched";
                if (ClassifyRequest(context) is { } kind)
                    NoCtfTelemetry.RecordApiRequest(route, outcome, Stopwatch.GetElapsedTime(started).TotalSeconds, kind);
                if (TryClassifyRuntimeOperation(
                        context.Request.Method,
                        route,
                        out var operation))
                    NoCtfTelemetry.RecordRuntimeOperation(operation, outcome);
            }
        });
        app.UseOpenTelemetryPrometheusScrapingEndpoint(context =>
            IsMetricsScrapeRequest(
                context.Request.Path,
                context.Connection.LocalPort,
                metricsPort));
        return app;
    }

    internal static bool IsMetricsScrapeRequest(
        PathString path,
        int localPort,
        int metricsPort) =>
        path == "/metrics" && localPort == metricsPort;

    internal static ApiRequestKind? ClassifyRequest(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs")) return ApiRequestKind.SignalR;
        if (!context.Request.Path.StartsWithSegments("/api")) return null;
        // Endpoint metadata applies even to rejected uploads/downloads; response headers do not.
        return context.GetEndpoint()?.Metadata.GetMetadata<ApiRequestMetricsMetadata>()?.Kind ?? ApiRequestKind.Rest;
    }

    internal static bool TryClassifyRuntimeOperation(
        string method,
        string route,
        out string operation)
    {
        if (!HttpMethods.IsPost(method))
        {
            operation = string.Empty;
            return false;
        }

        if (route.EndsWith("/flag-submissions", StringComparison.OrdinalIgnoreCase)
            || route.EndsWith("/awdp-break-flag-judgement", StringComparison.OrdinalIgnoreCase))
        {
            operation = "flag";
            return true;
        }
        if (route.EndsWith("/awdp-defense-targets", StringComparison.OrdinalIgnoreCase))
        {
            operation = "fix_request";
            return true;
        }
        if (route.Contains("/awdp-defense-targets/", StringComparison.OrdinalIgnoreCase)
            && route.EndsWith("/fix", StringComparison.OrdinalIgnoreCase))
        {
            operation = "fix_upload";
            return true;
        }

        var runtimeAction = RuntimeActions.FirstOrDefault(action =>
            route.EndsWith(action.Suffix, StringComparison.OrdinalIgnoreCase));
        if (runtimeAction is not null)
        {
            operation = runtimeAction.Operation;
            return true;
        }

        operation = string.Empty;
        return false;
    }

    private static readonly RuntimeAction[] RuntimeActions =
    [
        new("/test-runtime/start", "runtime_test_start"),
        new("/test-runtime/stop", "runtime_test_stop"),
        new("/test-runtime/reset", "runtime_test_reset"),
        new("/test-runtime/extend", "runtime_test_extend"),
        new("/runtime/start", "runtime_start"),
        new("/runtime/stop", "runtime_stop"),
        new("/runtime/reset", "runtime_reset"),
        new("/runtime/extend", "runtime_extend"),
        new("/force-terminate", "runtime_force_terminate"),
        new("/terminate", "runtime_terminate")
    ];

    private sealed record RuntimeAction(string Suffix, string Operation);
}

public sealed record ApiRequestMetricsMetadata(ApiRequestKind Kind);

file static class ThisAssemblyVersion
{
    public static readonly string Value = typeof(ThisAssemblyVersion).Assembly
        .GetName().Version?.ToString() ?? "unknown";
}
