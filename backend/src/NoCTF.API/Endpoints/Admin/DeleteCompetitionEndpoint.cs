using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Leaderboard;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteCompetitionEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    IContainerManager containerManager,
    IRedisLeaderboardCache leaderboardCache,
    IStorageProvider storageProvider) : Endpoint<EmptyRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Delete("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, id, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var competition = await dbContext.Competitions.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsedUserId))
            userId = parsedUserId;

        await ContainerCleanupRuntime.CleanupCompetitionAsync(
            dbContext,
            containerManager,
            id,
            HttpContext,
            userId,
            "competition_deleted",
            ct);

        var storageKeys = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == id)
            .Select(c => new { c.AttachmentStorageKey, c.PatchTemplateStorageKey })
            .ToListAsync(ct);
        await DeleteCompetitionArtifactsAsync(dbContext, id, ct);
        dbContext.Competitions.Remove(competition);
        await dbContext.SaveChangesAsync(ct);
        await StorageObjectCleanup.DeleteUnreferencedAsync(
            dbContext,
            storageProvider,
            storageKeys.SelectMany(k => new[] { k.AttachmentStorageKey, k.PatchTemplateStorageKey }),
            ct);
        await leaderboardCache.InvalidateAsync(id, ct);
        await SendNoContentAsync(ct);
    }

    internal static async Task DeleteCompetitionArtifactsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        db.TeamMembers.RemoveRange(await db.TeamMembers
            .Where(tm => tm.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.CompetitionCollaborators.RemoveRange(await db.CompetitionCollaborators
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.ChallengeHints.RemoveRange(await db.ChallengeHints
            .IgnoreQueryFilters()
            .Where(h => h.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.Submissions.RemoveRange(await db.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.ScoreEvents.RemoveRange(await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.ScoreSignals.RemoveRange(await db.ScoreSignals
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.CtfDynamicFlags.RemoveRange(await db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.DynamicFlagInstances.RemoveRange(await db.DynamicFlagInstances
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.CompetitionLogs.RemoveRange(await db.CompetitionLogs
            .IgnoreQueryFilters()
            .Where(l => l.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.CheatIncidents.RemoveRange(await db.CheatIncidents
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdRounds.RemoveRange(await db.AwdRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdFlags.RemoveRange(await db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdAttackRecords.RemoveRange(await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdCheckResults.RemoveRange(await db.AwdCheckResults
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdGameBoxes.RemoveRange(await db.AwdGameBoxes
            .IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdpRounds.RemoveRange(await db.AwdpRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdpTeamChallengeStates.RemoveRange(await db.AwdpTeamChallengeStates
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdpRoundScores.RemoveRange(await db.AwdpRoundScores
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.AwdpPatchSubmissions.RemoveRange(await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.PenetrationFlags.RemoveRange(await db.PenetrationFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.PenetrationNodes.RemoveRange(await db.PenetrationNodes
            .IgnoreQueryFilters()
            .Where(n => n.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.PenetrationTopologies.RemoveRange(await db.PenetrationTopologies
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.TeamChallengeInstances.RemoveRange(await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.KohControlRecords.RemoveRange(await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.BackgroundTasks.RemoveRange(await db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.Challenges.RemoveRange(await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId)
            .ToListAsync(ct));
        db.Teams.RemoveRange(await db.Teams
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId)
            .ToListAsync(ct));
    }
}
