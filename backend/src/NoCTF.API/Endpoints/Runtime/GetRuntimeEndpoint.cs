using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;
using System.Text.Json.Serialization;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;

namespace NoCTF.API.Endpoints.Runtime;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeKindProtocol>))]
public enum RuntimeKindProtocol
{
    Container,
    Compose,
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
    TemplateTest
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

[Mapper]
public static partial class RuntimeProtocolMapper
{
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
    IReadOnlyList<string> Urls,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RunningAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? StoppedAt)
{
    public RuntimeAccessResponse? Access { get; init; }
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<RuntimeAccessRouteProtocol>))]
public enum RuntimeAccessRouteProtocol { Direct, Gateway }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<PublicAccessStateProtocol>))]
public enum PublicAccessStateProtocol { Disabled, Pending, Ready, Unavailable, Revoking, Unsupported }
[JsonConverter(typeof(StrictPascalCaseEnumConverter<PublicAccessFailureProtocol>))]
public enum PublicAccessFailureProtocol
{
    GatewayDisabled, ConnectorOffline, PublicPortUnavailable, RuntimeBindingUnavailable, UnsupportedRuntimeKind,
    AccessDisplayUnsupported, GatewayCapacityExceeded, GatewayIdentityRejected, GatewaySafetyCheckFailed, GatewayReconciliationPending
}
public sealed record PublicEndpointResponse(int ContainerPort, int HostPort, PublicAccessStateProtocol State, PublicAccessFailureProtocol? Failure, int? PublicPort = null);
public sealed record RuntimeAccessResponse(RuntimeAccessRouteProtocol Route, PublicAccessStateProtocol State,
    PublicAccessFailureProtocol? Failure, IReadOnlyList<PublicEndpointResponse> Endpoints);
[Mapper]
internal static partial class RuntimeAccessMapping
{
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial RuntimeAccessRouteProtocol ToProtocol(RuntimeAccessRoute value);
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial PublicAccessStateProtocol ToProtocol(PublicAccessState value);
    [MapEnum(EnumMappingStrategy.ByName)]
    public static partial PublicAccessFailureProtocol ToProtocol(PublicAccessFailure value);
    public static RuntimeAccessResponse ToResponse(RuntimeAccessProjection access) => new(ToProtocol(access.Route),
        ToProtocol(access.State), access.Failure is { } failure ? ToProtocol(failure) : null,
        access.Endpoints.Select(item => new PublicEndpointResponse(item.ContainerPort, item.HostPort,
            ToProtocol(item.State), item.Failure is { } code ? ToProtocol(code) : null, item.PublicPort)).ToArray());
}

public sealed record RuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

internal static class RuntimeEndpointMapping
{
    public static ProblemHttpResult UnknownOrigin() => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Runtime access origin is not configured.", detail: "Use an origin configured by the platform administrator for direct or public access.");
    public static RuntimeResponse ToResponse(RuntimeInstanceView view, RuntimeAccessProjection? access = null) =>
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
            view.State == RuntimeState.Running ? access?.Urls ?? view.Urls : [],
            view.CreatedAt,
            view.RunningAt,
            view.ExpiresAt,
            view.StoppedAt) { Access = access is null ? null : RuntimeAccessMapping.ToResponse(access) };

    public static RuntimeAcceptedResponse ToAccepted(RuntimeInstanceView view) =>
        new(
            view.Id,
            $"/api/v1/competitions/{view.CompetitionId!.Value}/challenges/{view.CompetitionChallengeId!.Value}/runtime");
}

public sealed class GetRuntimeEndpoint(
    GetPlayerRuntime get,
    IUserContext user,
    ReadRuntimePublicAccess access)
    : EndpointWithoutRequest<Results<Ok<RuntimeResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Gets the current team runtime.";
            summary.Description = "Only Running instances expose expanded public URLs.";
        });
    }

    public override async Task<Results<Ok<RuntimeResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        if (view is null) return TypedResults.NotFound();
        var projection = await access.ExecuteAsync(view, $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}", ct);
        return projection is null ? RuntimeEndpointMapping.UnknownOrigin() : TypedResults.Ok(RuntimeEndpointMapping.ToResponse(view, projection));
    }
}
