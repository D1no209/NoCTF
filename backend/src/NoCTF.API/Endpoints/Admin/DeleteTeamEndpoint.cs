using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteTeamEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    IContainerManager containerManager,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier) : Endpoint<EmptyRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/teams/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var team = await dbContext.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, team.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsedUserId))
            userId = parsedUserId;

        await ContainerCleanupRuntime.CleanupTeamAsync(
            dbContext,
            containerManager,
            team.CompetitionId,
            team.Id,
            HttpContext,
            userId,
            "team_deleted",
            ct);

        await DeleteTeamArtifactsAsync(dbContext, team.CompetitionId, id, ct);
        dbContext.Teams.Remove(team);
        await dbContext.SaveChangesAsync(ct);
        await RefreshLeaderboardAsync(team.CompetitionId, ct);

        await SendNoContentAsync(ct);
    }

    internal static async Task DeleteTeamArtifactsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        db.TeamMembers.RemoveRange(await db.TeamMembers
            .Where(tm => tm.CompetitionId == competitionId && tm.TeamId == teamId)
            .ToListAsync(ct));
        db.Submissions.RemoveRange(await db.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.ScoreEvents.RemoveRange(await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.ScoreSignals.RemoveRange(await db.ScoreSignals
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.CtfDynamicFlags.RemoveRange(await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId)
            .ToListAsync(ct));
        db.DynamicFlagInstances.RemoveRange(await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdFlags.RemoveRange(await db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdAttackRecords.RemoveRange(await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && (r.AttackerTeamId == teamId || r.VictimTeamId == teamId))
            .ToListAsync(ct));
        db.AwdCheckResults.RemoveRange(await db.AwdCheckResults
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdGameBoxes.RemoveRange(await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId && g.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdpTeamChallengeStates.RemoveRange(await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdpRoundScores.RemoveRange(await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.AwdpPatchSubmissions.RemoveRange(await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct));
        db.TeamChallengeInstances.RemoveRange(await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && i.TeamId == teamId)
            .ToListAsync(ct));
        db.KohControlRecords.RemoveRange(await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.TeamId == teamId)
            .ToListAsync(ct));
        db.CheatIncidents.RemoveRange(await db.CheatIncidents
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && (i.SuspectTeamId == teamId || i.VictimTeamId == teamId))
            .ToListAsync(ct));
        db.CompetitionLogs.RemoveRange(await db.CompetitionLogs
            .IgnoreQueryFilters()
            .Where(l => l.CompetitionId == competitionId && l.TeamId == teamId)
            .ToListAsync(ct));
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, ct);
        await hubNotifier.NotifyLeaderboardSnapshotAsync(
            competitionId,
            entries.Select(e => new LeaderboardEntryPayload(
                e.Rank,
                e.TeamId,
                e.TeamName,
                e.TotalScore,
                e.SolvedCount)),
            ct);
    }
}
