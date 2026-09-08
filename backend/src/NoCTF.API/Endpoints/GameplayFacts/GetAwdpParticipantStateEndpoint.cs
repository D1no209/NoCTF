using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.API.Security;
using NoCTF.Application.GameplayFacts.Awdp;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.PublicAccess;

namespace NoCTF.API.Endpoints.GameplayFacts;

public sealed record AwdpAchievementActivationResponse(
    Guid GameplayFactId,
    int EffectiveRound,
    DateTimeOffset EffectiveAt);

public sealed record AwdpDefenseProgressResponse(
    Guid? RuntimeInstanceId,
    RuntimeStateProtocol? RuntimeState,
    RuntimeFailureCodeProtocol? RuntimeFailureCode,
    Guid? GameplayFactId,
    Guid? PatchUploadId,
    GameplayFactStateProtocol? State,
    GameplayFactResultProtocol? Result,
    GameplayFactFailureCodeProtocol? FailureCode,
    DateTimeOffset? TargetCreatedAt,
    DateTimeOffset? TargetExpiresAt,
    DateTimeOffset? TargetStoppedAt,
    DateTimeOffset? UpdatedAt);

public sealed record AwdpParticipantStateResponse(
    int? CurrentRound,
    RuntimeResponse? AttackRuntime,
    GameplayFactStatusResponse? LatestBreakAttempt,
    AwdpAchievementActivationResponse? BreakActivation,
    AwdpDefenseProgressResponse Defense,
    AwdpAchievementActivationResponse? FixActivation,
    int? MaximumFixAttempts,
    int AcceptedFixAttempts,
    int? RemainingFixAttempts);

internal static class AwdpParticipantStateMapping
{
    internal static AwdpParticipantStateResponse ToResponse(AwdpParticipantStateView view, RuntimeAccessProjection? access = null) =>
        new(
            view.CurrentRound,
            view.AttackRuntime is null ? null : RuntimeEndpointMapping.ToResponse(view.AttackRuntime, access),
            view.LatestBreakAttempt is null
                ? null
                : GameplayFactMapper.ToStatusResponse(view.LatestBreakAttempt),
            Activation(view.BreakActivation),
            new(
                view.Defense.RuntimeInstanceId,
                view.Defense.RuntimeState is null
                    ? null
                    : RuntimeProtocolMapper.ToProtocol(view.Defense.RuntimeState.Value),
                view.Defense.RuntimeFailureCode is null
                    ? null
                    : RuntimeProtocolMapper.ToProtocol(view.Defense.RuntimeFailureCode.Value),
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
                view.Defense.TargetCreatedAt,
                view.Defense.TargetExpiresAt,
                view.Defense.TargetStoppedAt,
                view.Defense.UpdatedAt),
            Activation(view.FixActivation),
            view.MaximumFixAttempts,
            view.AcceptedFixAttempts,
            view.RemainingFixAttempts);

    private static AwdpAchievementActivationResponse? Activation(
        AwdpAchievementActivationView? view) =>
        view is null ? null : new(view.GameplayFactId, view.EffectiveRound, view.EffectiveAt);
}

public sealed class GetAwdpParticipantStateEndpoint(
    GetAwdpParticipantState get,
    IUserContext user,
    TimeProvider timeProvider,
    ReadRuntimePublicAccess access)
    : EndpointWithoutRequest<Results<Ok<AwdpParticipantStateResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}/awdp-state");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Gets the current team's AWDP attack and defense state.";
            summary.Description =
                "Returns the current attack Runtime, Break activation, one-shot Fix progress, authoritative Fix attempt limits and defense activation.";
        });
    }

    public override async Task<Results<Ok<AwdpParticipantStateResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var view = await get.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (view is null) return TypedResults.NotFound();
        var projection = view.AttackRuntime is null ? null : await access.ExecuteAsync(view.AttackRuntime,
            $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}", cancellationToken);
        if (view.AttackRuntime is not null && projection is null) return RuntimeEndpointMapping.UnknownOrigin();
        return TypedResults.Ok(AwdpParticipantStateMapping.ToResponse(view, projection));
    }
}
