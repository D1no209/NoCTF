using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Administration.Monitoring;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.API.Endpoints.Runtime;

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
    int LatencySustainedWindowMinutes,
    HumanVerificationMonitoringResponse HumanVerification,
    RunnerCapacityReportResponse? Capacity = null);

public sealed record RunnerResourceAmountResponse(long MemoryBytes, long NanoCpus, long PidsLimit);
public sealed record RunnerObservedResourceAmountResponse(long MemoryBytes, long NanoCpus, long? PidsLimit);
public sealed record RunnerObservationResponse(DateTimeOffset ObservedAt, double CpuUsageRatio,
    long MemoryTotalBytes, long MemoryAvailableBytes, long NanoCpus,
    long? PidsUsed, long? PidsCapacity, long OomKills,
    bool ProviderPressure, bool PidPressureConditionAvailable);
public sealed record RunnerCapacitySnapshotResponse(string RunnerId, bool Alive,
    RunnerAdmissionStateProtocol State, RunnerAdmissionFailureProtocol? Failure,
    RunnerObservedResourceAmountResponse? ObservedTotal,
    RunnerObservedResourceAmountResponse? ObservedAvailable,
    RunnerObservedResourceAmountResponse? SafetyHeadroom,
    RunnerObservedResourceAmountResponse? StartupReserved,
    RunnerObservedResourceAmountResponse? AdmissionAvailable,
    RunnerResourceAmountResponse? DeclaredLimits,
    RunnerObservationResponse? Observation, int? StartingPrimary, int? StartingAuxiliary);
public sealed record RunnerCapacityReportResponse(bool Available, IReadOnlyList<RunnerCapacitySnapshotResponse> Runners, bool Truncated);

public sealed record HumanVerificationMonitoringResponse(
    HumanVerificationProvider Provider,
    bool Enabled,
    HumanVerificationMonitoringState State,
    DateTimeOffset? CheckedAt,
    long? LatencyMilliseconds);

public sealed class GetPlatformMonitoringEndpoint(
    ObservePlatformMonitoring monitoring,
    ObserveRunnerCapacity? capacity = null)
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
        var resources = capacity is null ? null : await capacity.ExecuteAsync(ct);
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
            view.LatencyDetails ?? [],
            view.PoolResources ?? [],
            view.LatencySustainedWindowMinutes,
            new HumanVerificationMonitoringResponse(
                view.HumanVerification?.Provider ?? HumanVerificationProvider.None,
                view.HumanVerification?.Enabled ?? false,
                view.HumanVerification?.State
                    ?? HumanVerificationMonitoringState.NotApplicable,
                view.HumanVerification?.CheckedAt,
                view.HumanVerification?.LatencyMilliseconds), resources is null ? null : Map(resources)));
    }

    private static RunnerResourceAmountResponse? Amount(RuntimeResourceAmount? value) =>
        value is null ? null : new(value.MemoryBytes, value.NanoCpus, value.PidsLimit);

    private static RunnerObservedResourceAmountResponse? ObservedAmount(RunnerObservedResourceAmount? value) =>
        value is null ? null : new(value.MemoryBytes, value.NanoCpus, value.PidsLimit);

    private static RunnerCapacityReportResponse Map(RunnerCapacityReport report) => new(report.Available,
        report.Runners.Select(runner => new RunnerCapacitySnapshotResponse(runner.RunnerId, runner.Alive,
            RuntimeProtocolMapper.ToProtocol(runner.State), runner.Failure is { } failure ? RuntimeProtocolMapper.ToProtocol(failure) : null,
            ObservedAmount(runner.ObservedTotal), ObservedAmount(runner.ObservedAvailable),
            ObservedAmount(runner.SafetyHeadroom), ObservedAmount(runner.StartupReserved),
            ObservedAmount(runner.AdmissionAvailable), Amount(runner.DeclaredLimits),
            runner.Observation is { } sample ? new RunnerObservationResponse(sample.ObservedAt, sample.CpuUsageRatio,
                sample.MemoryTotalBytes, sample.MemoryAvailableBytes, sample.NanoCpus,
                sample.PidsUsed, sample.PidsCapacity, sample.OomKills,
                sample.ProviderPressure, sample.PidPressureConditionAvailable) : null,
            runner.StartingPrimary, runner.StartingAuxiliary)).ToArray(), report.Truncated);
}
