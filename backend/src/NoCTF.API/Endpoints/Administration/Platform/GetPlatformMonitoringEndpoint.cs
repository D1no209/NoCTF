using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.Monitoring;

namespace NoCTF.API.Endpoints.Administration.Platform;

public sealed record PlatformMonitoringMetricResponse(
    PlatformMonitoringMetricKind Kind,
    PlatformMonitoringUnit Unit,
    double? Value,
    PlatformMonitoringStatus Status,
    double? SampleCount, int? MinimumSamples, int? WindowSeconds);

public sealed record PlatformMonitoringResponse(
    PlatformMonitoringStatus Status,
    bool PrometheusAvailable,
    bool NatsAvailable,
    DateTimeOffset CapturedAt,
    string? DashboardUrl,
    IReadOnlyList<PlatformMonitoringMetricResponse> Metrics,
    IReadOnlyList<PlatformMonitoringLatencyView> LatencyDetails,
    IReadOnlyList<PlatformMonitoringPoolResource> PoolResources,
    int LatencySustainedWindowMinutes);

public sealed class GetPlatformMonitoringEndpoint(
    ObservePlatformMonitoring monitoring)
    : EndpointWithoutRequest<Ok<PlatformMonitoringResponse>>
{
    public override void Configure()
    {
        Get("/admin/platform/monitoring");
        AuthSchemes("Bearer");
        Roles("Administrator");
        Description(builder => builder.WithName("AdminPlatformGetMonitoring"));
        Summary(summary =>
        {
            summary.Summary = "Returns a curated platform operations snapshot.";
            summary.Description =
                "Returns low-cardinality operational summaries without exposing the Prometheus scrape endpoint or arbitrary queries.";
        });
    }

    public override async Task<Ok<PlatformMonitoringResponse>> ExecuteAsync(
        CancellationToken ct)
    {
        var view = await monitoring.ExecuteAsync(ct);
        return TypedResults.Ok(new PlatformMonitoringResponse(
            view.Status,
            view.PrometheusAvailable,
            view.NatsAvailable,
            view.CapturedAt,
            view.DashboardUri?.AbsoluteUri,
            view.Metrics.Select(metric => new PlatformMonitoringMetricResponse(
                metric.Kind,
                metric.Unit,
                metric.Value,
                metric.Status, metric.SampleCount, metric.MinimumSamples, metric.WindowSeconds)).ToArray(),
            view.LatencyDetails ?? [], view.PoolResources ?? [], view.LatencySustainedWindowMinutes));
    }
}
