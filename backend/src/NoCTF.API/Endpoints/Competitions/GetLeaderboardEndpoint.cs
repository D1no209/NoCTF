using FastEndpoints;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Application.Scoring.Ports;

namespace NoCTF.API.Endpoints.Competitions;

public sealed class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class GetLeaderboardEndpoint(ILeaderboardStore leaderboard)
    : Endpoint<GetLeaderboardRequest, LeaderboardSnapshot>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetLeaderboardRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var snapshot = await leaderboard.GetAuthoritativeAsync(request.CompetitionId, cancellationToken);
        if (snapshot is null)
        {
            await HttpContext.Response.SendNotFoundAsync(cancellationToken);
            return;
        }
        await HttpContext.Response.SendAsync<LeaderboardSnapshot>(snapshot, StatusCodes.Status200OK, null, cancellationToken);
    }
}
