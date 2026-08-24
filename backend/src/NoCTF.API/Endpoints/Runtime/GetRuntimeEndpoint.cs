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
    AwdpAttack
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
    DateTimeOffset? StoppedAt);

public sealed record RuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

internal static class RuntimeEndpointMapping
{
    public static RuntimeResponse ToResponse(RuntimeInstanceView view) =>
        new(
            view.Id,
            view.CompetitionId,
            view.CompetitionChallengeId,
            view.TeamId,
            RuntimeProtocolMapper.ToProtocol(view.RuntimeKind),
            RuntimeProtocolMapper.ToProtocol(view.Provider),
            RuntimeProtocolMapper.ToProtocol(view.State),
            view.FailureCode is null
                ? null
                : RuntimeProtocolMapper.ToProtocol(view.FailureCode.Value),
            view.State == RuntimeState.Running ? view.Urls : [],
            view.CreatedAt,
            view.RunningAt,
            view.ExpiresAt,
            view.StoppedAt);

    public static RuntimeAcceptedResponse ToAccepted(RuntimeInstanceView view) =>
        new(
            view.Id,
            $"/api/v1/competitions/{view.CompetitionId}/challenges/{view.CompetitionChallengeId}/runtime");
}

public sealed class GetRuntimeEndpoint(
    GetPlayerRuntime get,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<RuntimeResponse>, NotFound>>
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

    public override async Task<Results<Ok<RuntimeResponse>, NotFound>> ExecuteAsync(CancellationToken ct)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(RuntimeEndpointMapping.ToResponse(view));
    }
}
