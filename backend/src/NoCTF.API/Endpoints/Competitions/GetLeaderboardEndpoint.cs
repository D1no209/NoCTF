using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(ILeaderboardCache leaderboard, IBackgroundWorkScheduler scheduler)
    : Endpoint<GetLeaderboardRequest, Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LeaderboardResponse>, Accepted<LeaderboardProcessingResponse>>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var snapshot = await leaderboard.GetAsync(request.CompetitionId, cancellationToken);
        if (snapshot is not null) return TypedResults.Ok(snapshot);
        await scheduler.EnqueueLeaderboardRefreshAsync(request.CompetitionId, cancellationToken);
        return TypedResults.Accepted<LeaderboardProcessingResponse>((string?)null, new(request.CompetitionId, "Processing"));
    }
}
