using FastEndpoints;
using NoCTF.API.Permissions;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;

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
    ICtfScoreRebuilder ctfScoreRebuilder,
    ICompetitionPermissionService permissions)
    : Endpoint<RebuildScoreboardRequest, RebuildScoreboardResponse>, IAuditableEndpoint
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

        await ctfScoreRebuilder.RebuildCompetitionAsync(req.Id, ct);
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(req.Id, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(req.Id, ct);
        await leaderboardCache.UpdateAsync(req.Id, entries, cacheVersion, ct);

        await SendAsync(new RebuildScoreboardResponse
        {
            CompetitionId = req.Id,
            Rows = entries.Count
        }, cancellation: ct);
    }
}
