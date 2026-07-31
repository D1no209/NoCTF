using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Management;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(
    ILeaderboardCache leaderboard,
    IBackendMessagePublisher messages,
    GetCompetition getCompetition)
    : Endpoint<GetLeaderboardRequest, Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Summary(s => s.Summary = "Get the cached leaderboard or queue an asynchronous refresh.");
    }

    public override async Task<Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        if (await getCompetition.ExecuteAsync(request.CompetitionId, false, cancellationToken) is null)
            return TypedResults.NotFound();
        var snapshot = await leaderboard.GetAsync(request.CompetitionId, cancellationToken);
        if (snapshot is not null)
        {
            if (snapshot.Stale)
                await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
            return TypedResults.Ok(snapshot);
        }
        var status = await leaderboard.GetStatusAsync(request.CompetitionId, cancellationToken);
        if (status.LastFailureAt is not null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Leaderboard projection is unavailable.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "leaderboard_projection_failed",
                    ["targetRevision"] = status.TargetRevision,
                    ["lastFailureAt"] = status.LastFailureAt
                });
        await messages.ProjectLeaderboardAsync(request.CompetitionId, cancellationToken);
        HttpContext.Response.Headers.RetryAfter = "2";
        var statusUrl = $"/api/v1/competitions/{request.CompetitionId}/leaderboard";
        return TypedResults.Accepted(statusUrl, new LeaderboardProcessingResponse(
            request.CompetitionId,
            LeaderboardProjectionState.Processing,
            status.TargetRevision,
            statusUrl));
    }
}
