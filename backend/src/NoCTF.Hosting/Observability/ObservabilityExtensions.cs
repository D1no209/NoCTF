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
                .AddMeter("Wolverine")
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
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/metrics")
                || context.Request.Path.StartsWithSegments("/health"))
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
                if (TryClassifyRuntimeOperation(route, out var operation))
                    NoCtfTelemetry.RecordRuntimeOperation(operation, outcome);
            }
        });
        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }

    private static bool TryClassifyRuntimeOperation(string route, out string operation)
    {
        if (route.Contains("flag", StringComparison.OrdinalIgnoreCase))
        {
            operation = "flag";
            return true;
        }
        if (route.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || route.Contains("fix", StringComparison.OrdinalIgnoreCase))
        {
            operation = "fix";
            return true;
        }
        if (route.Contains("runtime", StringComparison.OrdinalIgnoreCase))
        {
            operation = "runtime";
            return true;
        }

        operation = string.Empty;
        return false;
    }
}

file static class ThisAssemblyVersion
{
    public static readonly string Value = typeof(ThisAssemblyVersion).Assembly
        .GetName().Version?.ToString() ?? "unknown";
}
