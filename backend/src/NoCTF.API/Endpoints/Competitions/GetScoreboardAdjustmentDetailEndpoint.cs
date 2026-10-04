using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Tracks;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetScoreboardAdjustmentDetailRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }

    [QueryParam]
    public string? Cursor { get; set; }

    [QueryParam]
    public int Limit { get; set; } = 50;
}

public sealed class GetScoreboardAdjustmentDetailValidator
    : Validator<GetScoreboardAdjustmentDetailRequest>
{
    public GetScoreboardAdjustmentDetailValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 200);
}

public sealed record ScoreboardAdjustmentDetailResponse(
    Guid CompetitionId,
    Guid TeamId,
    int EntryCount,
    IReadOnlyList<ScoreboardActorResponse> Actors,
    IReadOnlyList<ScoreboardAdjustmentResponse> Items,
    string? NextCursor);

public sealed class GetScoreboardAdjustmentDetailEndpoint(
    ILeaderboardCache leaderboard,
    IScoreboardDetailReader details,
    ICompetitionVisibilityAccess access,
    GetCompetitionTracks getTracks,
    ICompetitionModerationAuthorizer authorizer,
    SignedKeysetCursor cursors,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<GetScoreboardAdjustmentDetailRequest,
        Results<Ok<ScoreboardAdjustmentDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>>
{
    private const string CursorEndpoint = "scoreboard.adjustment.detail";

    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/teams/{teamId}/adjustments");
        AllowAnonymous();
        Summary(summary => { summary.Summary = "Get global scoreboard adjustments with signed cursor pagination."; summary.Description = summary.Summary; });
    }

    public override async Task<Results<Ok<ScoreboardAdjustmentDetailResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetScoreboardAdjustmentDetailRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, timeProvider.GetUtcNow(), cancellationToken);
        if (visibility is null || visibility.DataScope == LeaderboardDataScope.Hidden)
            return TypedResults.NotFound();

        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is null)
            return Processing(request.CompetitionId);

        var canViewInternalTracks = user.UserId != Guid.Empty
            && await authorizer.CanJudgeAsync(user.UserId, request.CompetitionId, cancellationToken);
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
        var team = projection.Snapshot.Teams.SingleOrDefault(item => item.TeamId == request.TeamId);
        if (team is null)
            return TypedResults.NotFound();

        var dataAsOf = projection.Snapshot.DataAsOf ?? projection.Snapshot.GeneratedAt;
        var scope = string.Join(':',
            request.CompetitionId.ToString("N"),
            user.UserId.ToString("N"),
            request.TeamId.ToString("N"),
            projection.Schema.Revision,
            projection.Snapshot.Version,
            dataAsOf.UtcTicks);
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, scope, out var position))
        {
            return ApiProblems.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: ApiMessages.Get(ApiMessageId.InvalidCursor));
        }

        var adjustments = await details.ReadAdjustmentsAsync(new(
            request.CompetitionId,
            request.TeamId,
            dataAsOf,
            position?.CreatedAt,
            position?.Id,
            request.Limit + 1), cancellationToken);
        var page = adjustments.Take(request.Limit).ToArray();
        var actors = page
            .Where(item => item.ActorUserId is not null)
            .GroupBy(item => item.ActorUserId!.Value)
            .OrderBy(group => group.Key)
            .Select((group, index) => new ScoreboardActor(
                index,
                group.Key,
                group.Select(item => item.ActorDisplayName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "-"))
            .ToArray();
        var detailActorIndexes = actors.ToDictionary(
            actor => actor.UserId,
            actor => actor.Index);
        int? ActorIndex(Guid? actorUserId) => actorUserId is Guid value
            && detailActorIndexes.TryGetValue(value, out var index)
                ? index
                : null;
        var actorResponses = actors
            .Select(actor => new ScoreboardActorResponse(
                actor.Index,
                actor.UserId,
                actor.DisplayName))
            .ToArray();
        var items = page.Select(item =>
        {
            var earnedPoints = Math.Max(item.Delta, 0);
            var deductedPoints = Math.Max(-item.Delta, 0);
            return ScoreboardProtocolMapper.ToResponse(new ScoreboardAdjustment(
                item.Id,
                ScoreboardAdjustmentKind.ManualAdjustment,
                item.OccurredAt,
                ActorIndex(item.ActorUserId),
                earnedPoints,
                deductedPoints,
                item.Delta));
        }).ToArray();
        var nextCursor = adjustments.Count > request.Limit
            ? cursors.Encode(CursorEndpoint, scope, new(page[^1].OccurredAt, page[^1].Id))
            : null;
        return TypedResults.Ok(new ScoreboardAdjustmentDetailResponse(
            request.CompetitionId,
            request.TeamId,
            team.GlobalAdjustmentCount,
            actorResponses,
            items,
            nextCursor));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId,
            LeaderboardProjectionStateProtocol.Processing,
            statusUrl));
    }
}
