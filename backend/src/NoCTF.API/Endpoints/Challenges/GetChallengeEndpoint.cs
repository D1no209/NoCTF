using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.API.Endpoints.Competitions;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Domain.Platform;
using NoCTF.API.Endpoints.Runtime;

namespace NoCTF.API.Endpoints.Challenges;

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
    IReadOnlyList<string>? Urls = null,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live,
    int? MaximumFlagAttempts = null,
    int? AcceptedFlagAttempts = null,
    int? RemainingFlagAttempts = null,
    bool UsesDynamicFlag = false,
    IReadOnlyList<ParticipantChallengeHintResponse>? Hints = null)
{
    public PublicAccessFailureProtocol? PublicAccessFailure { get; init; }
}

public sealed record ParticipantChallengeHintResponse(
    Guid Id,
    long Cost,
    DateTimeOffset PublishedAt,
    string? Content,
    bool IsUnlocked,
    bool CanUnlock);

public sealed record ChallengeListResponse(
    IReadOnlyList<ChallengeResponse> Items,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live);

internal static class ChallengeMapper
{
    public static ChallengeResponse ToResponse(
        ChallengeView view,
        KohChallengeAccessView? koh = null,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live,
        FlagAttemptBudget? attemptBudget = null,
        IReadOnlyList<ParticipantChallengeHintView>? hints = null) =>
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
            koh?.Urls,
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope),
            attemptBudget?.Maximum,
            attemptBudget?.Accepted,
            attemptBudget?.Remaining,
            view.UsesDynamicFlag,
            hints?.Select(hint => new ParticipantChallengeHintResponse(
                hint.Id, hint.Cost, hint.PublishedAt, hint.Content, hint.IsUnlocked, hint.CanUnlock)).ToArray());

    public static ChallengeListResponse ToListResponse(
        IReadOnlyList<ChallengeView> views,
        CompetitionLeaderboardVisibility visibility = CompetitionLeaderboardVisibility.Normal,
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live) =>
        new(
            views.Select(view => ToResponse(
                view,
                visibility: visibility,
                dataScope: dataScope)).ToArray(),
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
    ICompetitionChallengeAudienceAccess audienceAccess,
    ICompetitionVisibilityAccess visibilityAccess,
    GetFlagAttemptBudget getAttemptBudget,
    ReadParticipantChallengeHints getHints,
    IUserContext user,
    TimeProvider timeProvider,
    ReadRuntimePublicAccess runtimeAccess) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Gets a published challenge.");
    }

    public override async Task<Results<Ok<ChallengeResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetChallengeRequest request,
        CancellationToken ct)
    {
        // The hint bodies are personalized by the viewer's team and must not enter shared caches.
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var competitionId = Route<Guid>("competitionId");
        if (!await audienceAccess.CanReadAsync(user.UserId, competitionId, ct))
            return TypedResults.NotFound();
        var visibility = await visibilityAccess.ResolveAsync(
            user.UserId,
            competitionId,
            timeProvider.GetUtcNow(),
            ct);
        if (visibility is null
            || !ParticipantChallengeVisibilityPolicy.CanView(
                visibility.CompetitionStatus))
            return TypedResults.NotFound();
        var item = await get.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            includeUnpublished: false,
            includeDeleted: false,
            ct);

        if (item is null)
            return TypedResults.NotFound();
        var access = await kohAccess.FindAsync(
            item.CompetitionId,
            item.Id,
            user.UserId,
            ct);
        var attemptBudget = user.UserId == Guid.Empty
            ? null
            : await getAttemptBudget.ExecuteAsync(
                item.CompetitionId,
                item.Id,
                user.UserId,
                ct);
        var response = ChallengeMapper.ToResponse(
            item,
            access,
            visibility.Visibility,
            visibility.DataScope,
            attemptBudget,
            await getHints.ExecuteAsync(item.CompetitionId, item.Id, user.UserId, timeProvider.GetUtcNow(), ct));
        if (access is null) return TypedResults.Ok(response);
        var route = await runtimeAccess.RouteAsync($"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}", ct);
        if (route is null) return RuntimeEndpointMapping.UnknownOrigin();
        return TypedResults.Ok(route == RuntimeAccessRoute.Direct ? response : response with
        { Urls = [], PublicAccessFailure = PublicAccessFailureProtocol.UnsupportedRuntimeKind });
    }
}
