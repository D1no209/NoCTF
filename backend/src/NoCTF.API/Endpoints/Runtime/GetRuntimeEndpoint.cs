using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;

namespace NoCTF.API.Endpoints.Runtime;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeKindProtocol>))]
public enum RuntimeKindProtocol
{
    Container,
    OvaVm
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeProviderProtocol>))]
public enum RuntimeProviderProtocol
{
    Docker,
    Kubernetes,
    Libvirt
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeStateProtocol>))]
public enum RuntimeStateProtocol
{
    Queued,
    Provisioning,
    Running,
    Stopping,
    Stopped,
    Failed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimePurposeProtocol>))]
public enum RuntimePurposeProtocol
{
    Player,
    AwdpTarget,
    Practice,
    AwdpAttack,
    TemplateTest,
    PatchVerificationTarget
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeFailureCodeProtocol>))]
public enum RuntimeFailureCodeProtocol
{
    InvalidConfiguration,
    RunnerUnavailable,
    ProviderUnavailable,
    ProvisionTimeout,
    ProviderRejected,
    CleanupFailed,
    UrlExpansionFailed
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RunnerAdmissionFailureProtocol>))]
public enum RunnerAdmissionFailureProtocol
{
    NoEligibleRunner, CpuActualCapacityInsufficient, MemoryActualCapacityInsufficient, PidActualCapacityInsufficient,
    NodePressureHigh, ObservationStale, LedgerRecovering, StartupConcurrencyLimited,
    ProviderUnavailable, RequestExceedsNodeCapacity
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RunnerAdmissionStateProtocol>))]
public enum RunnerAdmissionStateProtocol { Starting, Reconciling, Ready, PressureBlocked, ProviderUnavailable, Draining }

[Mapper]
public static partial class RuntimeProtocolMapper
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RunnerAdmissionFailureProtocol ToProtocol(RunnerAdmissionFailure value);
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RunnerAdmissionStateProtocol ToProtocol(RunnerAdmissionState value);
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeKindProtocol ToProtocol(RuntimeKind value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeProviderProtocol ToProtocol(RuntimeProvider value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeStateProtocol ToProtocol(RuntimeState value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimePurposeProtocol ToProtocol(RuntimePurpose value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeFailureCodeProtocol ToProtocol(RuntimeFailureCode value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeKind ToDomain(RuntimeKindProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeProvider ToDomain(RuntimeProviderProtocol value);

    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeState ToDomain(RuntimeStateProtocol value);
}

public sealed record RuntimeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid? TeamId,
    RuntimeKindProtocol RuntimeKind,
    RuntimeProviderProtocol Provider,
    RuntimeStateProtocol State,
    RuntimeFailureCodeProtocol? FailureCode,
    IReadOnlyList<RuntimeAccessResponse> Accesses,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt)
{
    public RunnerAdmissionFailureProtocol? WaitingReason { get; init; }
}

public sealed record RuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

public sealed record RuntimeConflictResponse(string Detail)
{
    public string Detail { get; init; } = ApiMessages.Localize(ApiMessageId.RuntimeStateConflict, Detail);
    public string MessageKey => ApiMessages.Key(ApiMessageId.RuntimeStateConflict);
    public IReadOnlyDictionary<string, object?> MessageArguments => ApiMessages.NoArguments;
}

public sealed record RuntimeAccessResponse(
    string? DirectAddress,
    string? WebSocketAddress);

internal static class RuntimeEndpointMapping
{
    public static RuntimeResponse ToResponse(
        RuntimeInstanceView view,
        HttpRequest request) =>
        new(
            view.Id,
            view.CompetitionId
                ?? throw new InvalidOperationException("Player Runtime has no CompetitionId."),
            view.CompetitionChallengeId
                ?? throw new InvalidOperationException("Player Runtime has no CompetitionChallengeId."),
            view.TeamId,
            RuntimeProtocolMapper.ToProtocol(view.RuntimeKind),
            RuntimeProtocolMapper.ToProtocol(view.Provider),
            RuntimeProtocolMapper.ToProtocol(view.State),
            view.FailureCode is null
                ? null
                : RuntimeProtocolMapper.ToProtocol(view.FailureCode.Value),
            view.State == RuntimeState.Running
                ? RuntimeAccessMapping.ToResponse(view, request)
                : [],
            view.CreatedAt,
            view.RunningAt,
            view.ExpiresAt,
            view.StoppedAt)
        {
            WaitingReason = view.WaitingReason is { } reason ? RuntimeProtocolMapper.ToProtocol(reason) : null
        };

    public static RuntimeAcceptedResponse ToAccepted(RuntimeInstanceView view) =>
        new(
            view.Id,
            $"/api/v1/competitions/{view.CompetitionId!.Value}/challenges/{view.CompetitionChallengeId!.Value}/runtimes/current");
}

public sealed class GetRuntimeEndpoint(
    GetPlayerRuntime get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<RuntimeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes/current");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Gets the current team runtime.";
            summary.Description = "Only Running instances expose expanded public URLs.";
        });
    }

    public override async Task<Results<Ok<RuntimeResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        if (view is null) return TypedResults.NotFound();
        return TypedResults.Ok(RuntimeEndpointMapping.ToResponse(
            view,
            HttpContext.Request));
    }
}

internal static class RuntimeAccessMapping
{
    public static IReadOnlyList<RuntimeAccessResponse> ToResponse(
        RuntimeInstanceView view,
        HttpRequest request) =>
        ToResponse(
            view.Id,
            view.AccessMode,
            view.AccessEndpoints ?? [],
            request);

    public static IReadOnlyList<RuntimeAccessResponse> ToResponse(
        Guid runtimeInstanceId,
        RuntimeAccessMode accessMode,
        IReadOnlyList<RuntimeAccessEndpointView> accessEndpoints,
        HttpRequest request)
    {
        return accessEndpoints
            .OrderBy(endpoint => endpoint.BindingIndex)
            .Select(endpoint => new RuntimeAccessResponse(
                endpoint.DirectAddress,
                SupportsWsrx(accessMode)
                    && !string.IsNullOrWhiteSpace(endpoint.TargetHost)
                    && endpoint.TargetPort is >= 1 and <= 65535
                        ? WebSocketAddress(request, runtimeInstanceId, endpoint.BindingIndex)
                        : null))
            .Where(access => access.DirectAddress is not null
                || access.WebSocketAddress is not null)
            .ToArray();
    }

    public static string WebSocketAddress(
        HttpRequest request,
        Guid runtimeInstanceId,
        int bindingIndex)
    {
        var scheme = request.IsHttps ? "wss" : "ws";
        return $"{scheme}://{request.Host}{request.PathBase}/api/v1/runtime-proxies/{runtimeInstanceId:D}/{bindingIndex}";
    }

    private static bool SupportsWsrx(RuntimeAccessMode mode) =>
        mode is RuntimeAccessMode.DirectAndWsrx or RuntimeAccessMode.WsrxOnly;
}
