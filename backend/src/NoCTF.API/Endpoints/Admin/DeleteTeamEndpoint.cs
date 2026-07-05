using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteTeamEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    IContainerManager containerManager,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    ICtfScoreRebuilder ctfScoreRebuilder,
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
        await ctfScoreRebuilder.RebuildCompetitionAsync(team.CompetitionId, ct);
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
        var submissions = await db.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct);
        var penetrationFlagIdsToRecount = submissions
            .Where(s => s.IsCorrect && s.PenetrationFlagId.HasValue)
            .Select(s => s.PenetrationFlagId!.Value)
            .Distinct()
            .ToList();
        db.Submissions.RemoveRange(submissions);
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
        var awdAttackRecords = await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && (r.AttackerTeamId == teamId || r.VictimTeamId == teamId))
            .ToListAsync(ct);
        await RemoveAwdAttackScoreArtifactsAsync(db, competitionId, teamId, awdAttackRecords, ct);
        db.AwdAttackRecords.RemoveRange(awdAttackRecords);
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
        var awdpPatchSubmissions = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .ToListAsync(ct);
        await RemoveAwdpPatchValidationTasksAsync(db, competitionId, awdpPatchSubmissions.Select(s => s.Id).ToList(), ct);
        db.AwdpPatchSubmissions.RemoveRange(awdpPatchSubmissions);
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

        await RecountPenetrationFlagsAsync(db, competitionId, teamId, penetrationFlagIdsToRecount, ct);
    }

    private static async Task RecountPenetrationFlagsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid deletedTeamId,
        IReadOnlyCollection<Guid> flagIds,
        CancellationToken ct)
    {
        if (flagIds.Count == 0)
            return;

        var flags = await db.PenetrationFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && flagIds.Contains(f.Id))
            .ToListAsync(ct);
        foreach (var flag in flags)
        {
            flag.SolvedCount = await db.Submissions
                .IgnoreQueryFilters()
                .Where(s =>
                    s.CompetitionId == competitionId &&
                    s.TeamId != deletedTeamId &&
                    s.IsCorrect &&
                    s.PenetrationFlagId == flag.Id)
                .Join(
                    db.Teams.IgnoreQueryFilters().Where(t =>
                        t.CompetitionId == competitionId &&
                        t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                        !t.IsBanned),
                    s => s.TeamId,
                    t => t.Id,
                    (s, _) => s.TeamId)
                .Distinct()
                .CountAsync(ct);
        }
    }

    private static async Task RemoveAwdAttackScoreArtifactsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid deletedTeamId,
        IReadOnlyCollection<AwdAttackRecord> removedRecords,
        CancellationToken ct)
    {
        if (removedRecords.Count == 0)
            return;

        var signalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in removedRecords)
        {
            signalKeys.Add($"awd:{record.RoundNumber}:{record.AttackerTeamId:N}:{record.VictimTeamId:N}:{record.ChallengeId:N}:attack");

            var remainingVictimAttacks = await db.AwdAttackRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(r =>
                    r.CompetitionId == competitionId &&
                    r.Id != record.Id &&
                    r.AttackerTeamId != deletedTeamId &&
                    r.VictimTeamId == record.VictimTeamId &&
                    r.ChallengeId == record.ChallengeId &&
                    r.RoundNumber == record.RoundNumber, ct);
            if (!remainingVictimAttacks)
                signalKeys.Add($"awd:{record.RoundNumber}:{record.VictimTeamId:N}:{record.ChallengeId:N}:been-attacked");
        }

        var eventKeys = signalKeys.Select(key => $"round:{key}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        db.ScoreSignals.RemoveRange(await db.ScoreSignals
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && signalKeys.Contains(s.IdempotencyKey))
            .ToListAsync(ct));
        db.ScoreEvents.RemoveRange(await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && eventKeys.Contains(s.IdempotencyKey))
            .ToListAsync(ct));
    }

    private static async Task RemoveAwdpPatchValidationTasksAsync(
        ApplicationDbContext db,
        Guid competitionId,
        IReadOnlyCollection<Guid> submissionIds,
        CancellationToken ct)
    {
        if (submissionIds.Count == 0)
            return;

        var submissionIdText = submissionIds.Select(id => id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tasks = await db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId && t.Type == "awdp.patch.validation")
            .ToListAsync(ct);
        db.BackgroundTasks.RemoveRange(tasks.Where(t =>
            submissionIdText.Any(id => t.PayloadJson.Contains(id, StringComparison.OrdinalIgnoreCase))));
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
