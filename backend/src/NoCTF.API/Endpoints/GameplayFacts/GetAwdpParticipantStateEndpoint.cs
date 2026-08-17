using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Runtime;
using Riok.Mapperly.Abstractions;

namespace NoCTF.API.Endpoints.GameplayFacts;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<AwdpFixStageProtocol>))]
public enum AwdpFixStageProtocol
{
    TargetProvisioning,
    PatchApplying,
    CheckerRunning,
    Completed
}

public sealed record AwdpAchievementActivationResponse(
    Guid GameplayFactId,
    int EffectiveRound,
    DateTimeOffset EffectiveAt);

public sealed record AwdpDefenseProgressResponse(
    Guid? GameplayFactId,
    Guid? PatchUploadId,
    GameplayFactStateProtocol? State,
    GameplayFactResultProtocol? Result,
    GameplayFactFailureCodeProtocol? FailureCode,
    AwdpFixStageProtocol? Stage,
    DateTimeOffset? UpdatedAt);

public sealed record AwdpParticipantStateResponse(
    int? CurrentRound,
    RuntimeResponse? AttackRuntime,
    GameplayFactStatusResponse? LatestBreakAttempt,
    AwdpAchievementActivationResponse? BreakActivation,
    AwdpDefenseProgressResponse Defense,
    AwdpAchievementActivationResponse? FixActivation);

[Mapper]
internal static partial class AwdpParticipantStateMapping
{
    [MapEnum(EnumMappingStrategy.ByName)]
    private static partial AwdpFixStageProtocol ToProtocol(AwdpFixStage value);

    internal static AwdpParticipantStateResponse ToResponse(AwdpParticipantStateView view) =>
        new(
            view.CurrentRound,
            view.AttackRuntime is null ? null : RuntimeEndpointMapping.ToResponse(view.AttackRuntime),
            view.LatestBreakAttempt is null
                ? null
                : GameplayFactMapper.ToStatusResponse(view.LatestBreakAttempt),
            Activation(view.BreakActivation),
            new(
                view.Defense.GameplayFactId,
                view.Defense.PatchUploadId,
                view.Defense.State is null
                    ? null
                    : GameplayFactMapper.ToProtocol(view.Defense.State.Value),
                view.Defense.Result is null
                    ? null
                    : GameplayFactMapper.ToProtocol(view.Defense.Result.Value),
                view.Defense.FailureCode is null
                    ? null
                    : GameplayFactMapper.ToProtocol(view.Defense.FailureCode.Value),
                view.Defense.Stage is null ? null : ToProtocol(view.Defense.Stage.Value),
                view.Defense.UpdatedAt),
            Activation(view.FixActivation));

    private static AwdpAchievementActivationResponse? Activation(
        AwdpAchievementActivationView? view) =>
        view is null ? null : new(view.GameplayFactId, view.EffectiveRound, view.EffectiveAt);
}

public sealed class GetAwdpParticipantStateEndpoint(
    GetAwdpParticipantState get,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Ok<AwdpParticipantStateResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Gets the current team's AWDP attack and defense state.";
            summary.Description =
                "Returns the current attack Runtime, Break activation, one-shot Fix stage and defense activation.";
        });
    }

    public override async Task<Results<Ok<AwdpParticipantStateResponse>, NotFound>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(AwdpParticipantStateMapping.ToResponse(view));
    }
}
