using System.Text.Json.Serialization;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<ScoreboardRoundStateProtocol>))]
public enum ScoreboardRoundStateProtocol { Pending, Running, Settling, Settled }

public sealed record ScoreboardRoundResponse(
    Guid Id,
    int Number,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    DateTimeOffset? SettledAt,
    ScoreboardRoundStateProtocol State);

public sealed record ScoreboardColumnResponse(
    int Index,
    Guid CompetitionChallengeId,
    Guid? RoundId);

public sealed record ScoreboardSchemaResponse(
    Guid CompetitionId,
    GameModeProtocol Mode,
    long Revision,
    long ChallengeCatalogRevision,
    IReadOnlyList<ScoreboardRoundResponse> Rounds,
    IReadOnlyList<ScoreboardColumnResponse> Columns);

public sealed class GetScoreboardSchemaRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetScoreboardSchemaEndpoint(
    ILeaderboardCache leaderboard,
    ICompetitionVisibilityAccess access,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<GetScoreboardSchemaRequest, Results<Ok<ScoreboardSchemaResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard/schema");
        AllowAnonymous();
        Summary(summary => summary.Summary = "Get the low-frequency scoreboard matrix schema.");
    }

    public override async Task<Results<Ok<ScoreboardSchemaResponse>, Accepted<LeaderboardProcessingProtocolResponse>, NotFound>> ExecuteAsync(
        GetScoreboardSchemaRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var visibility = await access.ResolveAsync(
            user.UserId, request.CompetitionId, DateTimeOffset.UtcNow, cancellationToken);
        if (visibility is null)
            return TypedResults.NotFound();
        if (visibility.DataScope == LeaderboardDataScope.Hidden)
        {
            return TypedResults.Ok(new ScoreboardSchemaResponse(
                request.CompetitionId,
                Enum.Parse<GameModeProtocol>(visibility.GameMode.ToString()),
                0,
                0,
                [],
                []));
        }
        var projection = visibility.DataScope == LeaderboardDataScope.Frozen
            ? await leaderboard.GetFrozenScoreboardAsync(request.CompetitionId, cancellationToken)
            : await leaderboard.GetScoreboardAsync(request.CompetitionId, cancellationToken);
        if (projection is null)
            return Processing(request.CompetitionId);
        var canObserve = user.UserId != Guid.Empty
            && await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, cancellationToken);
        var schema = ScoreboardAudienceProjection.Filter(projection, canObserve).Schema;
        return TypedResults.Ok(new ScoreboardSchemaResponse(
            schema.CompetitionId,
            Enum.Parse<GameModeProtocol>(schema.Mode.ToString()),
            schema.Revision,
            schema.ChallengeCatalogRevision,
            schema.Rounds.Select(round => new ScoreboardRoundResponse(
                round.Id,
                round.Number,
                round.StartAt,
                round.EndAt,
                round.SettledAt,
                Enum.Parse<ScoreboardRoundStateProtocol>(round.State.ToString()))).ToArray(),
            schema.Columns.Select(column => new ScoreboardColumnResponse(
                column.Index, column.CompetitionChallengeId, column.RoundId)).ToArray()));
    }

    private Accepted<LeaderboardProcessingProtocolResponse> Processing(Guid competitionId)
    {
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{competitionId}/leaderboard/schema";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingProtocolResponse(
            competitionId, LeaderboardProjectionStateProtocol.Processing, statusUrl));
    }
}
