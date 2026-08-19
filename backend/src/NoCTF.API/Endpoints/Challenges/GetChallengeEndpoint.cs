using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Koh;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.API.Endpoints.Competitions;

namespace NoCTF.API.Endpoints.Challenges;

public sealed record ChallengeResponse(
    Guid Id,
    Guid CompetitionId,
    Guid ChallengeId,
    string Title,
    string? CustomTitle,
    string? Description,
    string Direction,
    long? BaseScore,
    int Order,
    bool IsPublished,
    int Revision,
    DateTimeOffset? DeletedAt,
    bool HasRuntime,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ControlFlag = null,
    IReadOnlyList<string>? Urls = null,
    LeaderboardVisibilityProtocol LeaderboardVisibility = LeaderboardVisibilityProtocol.Normal,
    LeaderboardDataScopeProtocol DataScope = LeaderboardDataScopeProtocol.Live);

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
        LeaderboardDataScope dataScope = LeaderboardDataScope.Live) =>
        new(
            view.Id,
            view.CompetitionId,
            view.ChallengeId,
            view.Title,
            view.CustomTitle,
            view.Description,
            view.Direction,
            dataScope == LeaderboardDataScope.Hidden ? null : view.BaseScore,
            view.Order,
            view.IsPublished,
            view.Revision,
            view.DeletedAt,
            view.HasRuntime,
            view.CreatedAt,
            view.UpdatedAt,
            koh?.ControlFlag,
            koh?.Urls,
            CompetitionProtocolMapper.ToProtocol(visibility),
            ScoreboardProtocolMapper.ToProtocol(dataScope));

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
    ICompetitionVisibilityAccess visibilityAccess,
    IUserContext user) : Endpoint<GetChallengeRequest, Results<Ok<ChallengeResponse>, NotFound>>
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
        var competitionId = Route<Guid>("competitionId");
        var visibility = await visibilityAccess.ResolveAsync(
            user.UserId,
            competitionId,
            DateTimeOffset.UtcNow,
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
        return TypedResults.Ok(ChallengeMapper.ToResponse(
            item,
            access,
            visibility.Visibility,
            visibility.DataScope));
    }
}
