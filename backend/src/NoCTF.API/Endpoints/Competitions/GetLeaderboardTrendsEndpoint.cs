using System.Globalization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Competitions;

public sealed record ScoreboardTrendPointResponse(DateTimeOffset At, long Score);

public sealed record ScoreboardTeamTrendResponse(
    Guid TeamId,
    string TeamName,
    string TrackKey,
    IReadOnlyList<ScoreboardTrendPointResponse> Points);

public sealed record ScoreboardTrendsResponse(
    Guid CompetitionId,
    string Version,
    DateTimeOffset GeneratedAt,
    DateTimeOffset DataAsOf,
    IReadOnlyList<ScoreboardTeamTrendResponse> Teams);

public sealed class GetLeaderboardTrendsRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardTrendsEndpoint(
    ILeaderboardCache leaderboard,
    IBackendMessagePublisher messages,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    BuildScoreboardTrends buildTrends,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetLeaderboardTrendsRequest,
        Results<Ok<ScoreboardTrendsResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/trends");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get CTF team score trends from the authoritative scoreboard projection.");
    }

    public override async Task<Results<Ok<ScoreboardTrendsResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardTrendsRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId,
            request.CompetitionId,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (visibility is null || visibility.GameMode != GameMode.Ctf)
            return TypedResults.NotFound();

        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            var now = timeProvider.GetUtcNow();
            return TypedResults.Ok(new ScoreboardTrendsResponse(
                request.CompetitionId,
                "0",
                now,
                now,
                []));
        }

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is null)
        {
            if (visibility.DataScope == LeaderboardDataScope.Frozen)
            {
                await messages.ApplyCompetitionVisibilityAsync(
                    request.CompetitionId,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
                return Processing(request.CompetitionId);
            }

            var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
            if (status.LastFailureAt is not null)
            {
                return ApiProblems.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: ApiMessages.Get(ApiMessageId.GetLeaderboardTrendsTitleLeaderboardProjectionUnavailable),
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = LeaderboardProblemCode.LeaderboardProjectionFailed
                    });
            }

            await leaderboard.InvalidateAsync(request.CompetitionId, cancellationToken);
            return Processing(request.CompetitionId);
        }

        var canViewInternalTracks = user.UserId != Guid.Empty
            && await authorizer.CanJudgeAsync(
                user.UserId,
                request.CompetitionId,
                cancellationToken);
        var tracks = await getTracks.ExecuteAsync(
            request.CompetitionId,
            user.UserId == Guid.Empty ? null : user.UserId,
            canViewInternalTracks,
            includeInvitationCodes: false,
            cancellationToken);
        if (tracks is null)
            return TypedResults.NotFound();

        projection = ScoreboardAudienceProjection.ForPublishedChallenges(projection);
        projection = ScoreboardAudienceProjection.FilterTracks(
            projection,
            tracks,
            canViewInternalTracks);
        projection = projection with
        {
            Snapshot = projection.Snapshot with
            {
                Visibility = visibility.Visibility,
                DataScope = visibility.DataScope,
                DataAsOf = visibility.DataScope == LeaderboardDataScope.Frozen
                    ? projection.Snapshot.DataAsOf
                    : projection.Snapshot.GeneratedAt
            }
        };
        var trends = await buildTrends.ExecuteAsync(projection, cancellationToken);
        return trends is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(trends));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard/trends";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId,
            LeaderboardProjectionStateProtocol.Processing,
            statusUrl));
    }

    private static ScoreboardTrendsResponse ToResponse(ScoreboardTrends trends) => new(
        trends.CompetitionId,
        trends.Version.ToString(CultureInfo.InvariantCulture),
        trends.GeneratedAt,
        trends.DataAsOf,
        trends.Teams.Select(team => new ScoreboardTeamTrendResponse(
            team.TeamId,
            team.TeamName,
            team.TrackKey,
            team.Points.Select(point => new ScoreboardTrendPointResponse(
                point.At,
                point.Score)).ToArray())).ToArray());
}
