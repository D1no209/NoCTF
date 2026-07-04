using FastEndpoints;
using NoCTF.API.Permissions;
using NoCTF.Application.Leaderboard;

namespace NoCTF.API.Endpoints.Admin;

public class RebuildScoreboardRequest
{
    public Guid Id { get; set; }
}

public class RebuildScoreboardResponse
{
    public Guid CompetitionId { get; set; }
    public int Rows { get; set; }
}

public class RebuildScoreboardEndpoint(
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    ICompetitionPermissionService permissions)
    : Endpoint<RebuildScoreboardRequest, RebuildScoreboardResponse>
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{id}/scoreboard/rebuild");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(RebuildScoreboardRequest req, CancellationToken ct)
    {
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, req.Id, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var entries = await leaderboardService.CalculateLeaderboardAsync(req.Id, ct);
        await leaderboardCache.UpdateAsync(req.Id, entries, ct);

        await SendAsync(new RebuildScoreboardResponse
        {
            CompetitionId = req.Id,
            Rows = entries.Count
        }, cancellation: ct);
    }
}
