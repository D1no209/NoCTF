using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(ILeaderboardStore leaderboard)
    : Endpoint<GetLeaderboardRequest, Results<Ok<LeaderboardSnapshot>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LeaderboardSnapshot>, NotFound>> ExecuteAsync(
        GetLeaderboardRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var snapshot = await leaderboard.GetAuthoritativeAsync(request.CompetitionId, cancellationToken);
        if (snapshot is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(snapshot);
    }
}
