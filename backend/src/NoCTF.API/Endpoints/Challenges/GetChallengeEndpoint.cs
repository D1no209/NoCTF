using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Application.Competitions.Progression;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Challenges.Hints;
using NoCTF.API.Endpoints.Runtime;
using NoCTF.Domain.Challenges;
using NoCTF.Application.Challenges.Bank;
using NoCTF.API.Endpoints.GameplayFacts;
using NoCTF.API.Serialization;
using System.Text.Json.Serialization;
using NoCTF.Application.Challenges.Actions;

namespace NoCTF.API.Endpoints.Challenges;

public sealed record CompetitionChallengeTimingResponse(DateTimeOffset? AutoOpenAt, DateTimeOffset? ScoringEndsAt,
    DateTimeOffset? SubmissionDeadlineAt,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeOpeningState>))] ChallengeOpeningState OpeningState,
    bool RecalculationPending)
{
    public DateTimeOffset? ServerTime { get; init; }
    [JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeTimingPhase>))]
    public ChallengeTimingPhase? Phase { get; init; }
    internal static CompetitionChallengeTimingResponse From(ChallengeTiming timing, ChallengeOpeningState openingState = ChallengeOpeningState.None,
        bool pending = false) => new(timing.AutoOpenAt, timing.ScoringEndsAt, timing.SubmissionDeadlineAt, openingState, pending);
}

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string? Description,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset? DeletedAt,
    bool HasRuntime,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ControlFlag = null,
    IReadOnlyList<RuntimeAccessResponse>? Accesses = null,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live,
    int? MaximumFlagAttempts = null,
    int? AcceptedFlagAttempts = null,
    int? RemainingFlagAttempts = null,
    bool SolvedByMyTeam = false,
    bool UsesDynamicFlag = false,
    IReadOnlyList<ParticipantChallengeHintResponse>? Hints = null,
    Guid? DirectionId = null,
    string? DirectionIcon = null)
{
    public IReadOnlyList<AvailableChallengeActionResponse> AvailableActions { get; init; } = [];
    public CompetitionChallengeTimingResponse? Timing { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed record AvailableChallengeActionResponse(
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeActionKind>))] ChallengeActionKind Kind,
    bool Enabled,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<ChallengeActionAvailability>))] ChallengeActionAvailability Availability,
    [property: JsonConverter(typeof(StrictPascalCaseEnumConverter<GameplayFactAdmissionFailureCode>))] GameplayFactAdmissionFailureCode? AdmissionFailure,
    Guid? RuntimeInstanceId, int? RemainingAttempts, Guid? GameplayFactId)
{
    public string? AdmissionMessageKey => AdmissionFailure is { } failure ? ApiMessages.For(failure).Key : null;
}

public sealed record ParticipantChallengeHintResponse(
    Guid Id,
    long Cost,
    DateTimeOffset PublishedAt,
    string? Content,
    bool IsUnlocked,
    bool CanUnlock);

public sealed record ChallengeListResponse(
    IReadOnlyList<ChallengeSummaryResponse> Items,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live);

public sealed record ChallengeSummaryResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string Direction,
    int Order,
    bool IsPublished,
    DateTimeOffset? DeletedAt,
    bool Locked = false,
    int PrerequisitesSatisfied = 0,
    int PrerequisitesTotal = 0,
    Guid? DirectionId = null,
    string? DirectionIcon = null)
{
    public CompetitionChallengeTimingResponse? Timing { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}

internal static class ChallengeMapper
{
    public static ChallengeResponse ToResponse(
        ChallengeView view,
        KohChallengeAccessView? koh = null,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live,
        FlagAttemptState? attemptState = null,
        IReadOnlyList<ParticipantChallengeHintView>? hints = null,
        HttpRequest? request = null) =>
        new(
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.CustomTitle,
            view.Description,
            view.Direction,
            view.Order,
            view.IsPublished,
            view.DeletedAt,
            view.HasRuntime,
            view.CreatedAt,
            view.UpdatedAt,
            koh?.ControlFlag,
            koh is not null && request is not null
                ? RuntimeAccessMapping.ToResponse(
                    koh.RuntimeInstanceId,
                    koh.AccessMode,
                    koh.AccessEndpoints,
                    request)
                : null,
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope),
            attemptState?.Maximum,
            attemptState?.Accepted,
            attemptState?.Remaining,
            attemptState?.Solved == true,
            view.UsesDynamicFlag,
            hints?.Select(hint => new ParticipantChallengeHintResponse(
                hint.Id, hint.Cost, hint.PublishedAt, hint.Content, hint.IsUnlocked, hint.CanUnlock)).ToArray(),
            view.DirectionId,
            view.DirectionIcon) { Tags = view.Tags, Timing = CompetitionChallengeTimingResponse.From(view.Timing, view.OpeningState, view.TimingRecalculationPending) };

    public static ChallengeListResponse ToListResponse(
        IReadOnlyList<CompetitionChallengeSummaryView> views,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live) =>
        new(
            views.Select(view => new ChallengeSummaryResponse(
                view.Id,
                view.CompetitionId,
                view.ChallengeId,
                view.Title,
                view.CustomTitle,
                view.Direction,
                view.Order,
                view.IsPublished,
                view.DeletedAt,
                DirectionId: view.DirectionId, DirectionIcon: view.DirectionIcon) { Tags = view.Tags, Timing = CompetitionChallengeTimingResponse.From(view.Timing) }).ToArray(),
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope));
}

public sealed class GetChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class GetChallengeEndpoint(
    GetChallenge get,
    IKohChallengeAccessReader kohAccess,
    ICompetitionChallengeReadAccess readAccess,
    IProgressionChallengeAccess progressionAccess,
    GetFlagAttemptState getAttemptState,
    ReadParticipantChallengeHints getHints,
    GetAvailableChallengeActions actions,
    IUserContext user,
    TimeProvider timeProvider) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Gets a published challenge.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        // The hint bodies are personalized by the viewer's team and must not enter shared caches.
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var competitionId = Route<Guid>("competitionId");
        var decision = await readAccess.ResolveAsync(
            user.UserId,
            competitionId,
            timeProvider.GetUtcNow(),
            ct);
        if (decision is null
            || !ParticipantChallengeVisibilityPolicy.CanView(
                decision.Visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var visibility = decision.Visibility;
        var challengeInstanceId = Route<Guid>("competitionChallengeId");
        if (visibility.GameMode == GameMode.Ctf
            && decision.ProgressionEnabled
            && !await progressionAccess.IsActiveAsync(
                competitionId, challengeInstanceId, decision.TeamId, ct))
            return TypedResults.NotFound();
        var item = await get.ExecuteAsync(
            competitionId,
            challengeInstanceId,
            includeUnpublished: false,
            includeDeleted: false,
            ct);

        if (item is null)
            return TypedResults.NotFound();
        var access = visibility.GameMode == GameMode.Koh
            ? await kohAccess.FindAsync(
                item.CompetitionId,
                item.Id,
                user.UserId,
                ct)
            : null;
        var attemptState = decision.TeamId is not Guid teamId
            ? null
            : await getAttemptState.ExecuteAsync(
                item.CompetitionId,
                item.Id,
                teamId,
                visibility.GameMode,
                visibility.CompetitionStatus,
                ct);
        var response = ChallengeMapper.ToResponse(
            item,
            access,
            visibility.Visibility,
            visibility.DataScope,
            attemptState,
            await getHints.ExecuteAsync(
                item.CompetitionId, item.Id, visibility.CompetitionStatus,
                decision.TeamId, timeProvider.GetUtcNow(), ct),
            HttpContext.Request);
        var timingNow = timeProvider.GetUtcNow();
        response = response with { AvailableActions = (await actions.ExecuteAsync(item.CompetitionId, item.Id, user.UserId, timingNow, ct))
                .Select(action => new AvailableChallengeActionResponse(action.Kind, action.Enabled, action.Availability,
                    action.AdmissionFailure, action.RuntimeInstanceId, action.RemainingAttempts, action.GameplayFactId)).ToArray(),
            Timing = response.Timing is null ? null : response.Timing with {
            ServerTime = timingNow, Phase = item.Timing.Phase(timingNow,
                visibility.GameMode == GameMode.Ctf && visibility.CompetitionStatus == CompetitionStatus.Finished) } };
        return TypedResults.Ok(response);
    }

}
