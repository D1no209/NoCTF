using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Observability;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NoCTF.Hosting.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddNoCtfObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: ThisAssemblyVersion.Value)
                .AddAttributes([
                    new KeyValuePair<string, object>("deployment.environment",
                        configuration["DOTNET_ENVIRONMENT"] ?? "Production")
                ]))
            .WithMetrics(metrics => metrics
                .AddMeter(NoCtfTelemetry.MeterName)
                .AddMeter("Wolverine*")
                .AddMeter("Npgsql")
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

        return services;
    }

    public static WebApplication UseNoCtfObservability(this WebApplication app)
    {
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
                NoCtfTelemetry.RecordApiRequest(
                    route,
                    outcome,
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
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
            || route.EndsWith("/practice-flag", StringComparison.OrdinalIgnoreCase)
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
        new("/runtime/start", "runtime_start"),
        new("/runtime/stop", "runtime_stop"),
        new("/runtime/reset", "runtime_reset"),
        new("/runtime/extend", "runtime_extend"),
        new("/force-terminate", "runtime_force_terminate"),
        new("/terminate", "runtime_terminate")
    ];

    private sealed record RuntimeAction(string Suffix, string Operation);
}

file static class ThisAssemblyVersion
{
    public static readonly string Value = typeof(ThisAssemblyVersion).Assembly
        .GetName().Version?.ToString() ?? "unknown";
}
