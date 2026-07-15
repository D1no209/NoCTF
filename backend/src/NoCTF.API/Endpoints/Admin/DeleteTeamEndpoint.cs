using FastEndpoints;
using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
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
    ICompetitionExecutionLease executionLease,
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

        await using (var preparationLease = await executionLease.TryAcquireAsync(
            dbContext,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            team.CompetitionId,
            ct))
        {
            if (preparationLease is null)
            {
                await SendStringAsync("runtime_preparation_in_progress", 409, cancellation: ct);
                return;
            }

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            team.IsBanned = true;
            team.BannedAt ??= DateTime.UtcNow;
            team.BannedReason = "team_deleted";
            await dbContext.SaveChangesAsync(preparationCts.Token);
        }

        await leaderboardCache.InvalidateAsync(team.CompetitionId, ct);

        await ContainerCleanupRuntime.CleanupTeamAsync(
            dbContext,
            containerManager,
            executionLease,
            team.CompetitionId,
            team.Id,
            HttpContext,
            userId,
            "team_deleted",
            ct);

        var patchArchiveKeys = await dbContext.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == team.CompetitionId &&
                submission.TeamId == id)
            .Select(submission => submission.PatchArchiveUrl)
            .ToListAsync(ct);

        await using var cleanupTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;
        await DeleteTeamArtifactsAsync(dbContext, team.CompetitionId, id, ct);
        dbContext.Teams.Remove(team);
        await StorageObjectCleanup.EnqueueAsync(dbContext, patchArchiveKeys, ct);
        await dbContext.SaveChangesAsync(ct);
        await ctfScoreRebuilder.RebuildCompetitionAsync(team.CompetitionId, ct);
        if (cleanupTransaction is not null)
            await cleanupTransaction.CommitAsync(ct);
        await RefreshLeaderboardAsync(team.CompetitionId, ct);

        await SendNoContentAsync(ct);
    }

    internal static async Task DeleteTeamArtifactsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        CancellationToken ct)
    {
        await DeleteCompetitionEndpoint.DeleteAsync(
            db,
            db.TeamMembers.IgnoreQueryFilters().Where(tm =>
                tm.CompetitionId == competitionId && tm.TeamId == teamId),
            ct);
        var penetrationFlagIdsToRecount = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .Where(s => s.IsCorrect && s.PenetrationFlagId.HasValue)
            .Select(s => s.PenetrationFlagId!.Value)
            .Distinct()
            .ToListAsync(ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.Submissions.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreEvents.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreSignals.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.CtfDynamicFlags.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.DynamicFlagInstances.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdFlags.IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == teamId), ct);
        var awdAttackRecords = await db.AwdAttackRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.CompetitionId == competitionId && (r.AttackerTeamId == teamId || r.VictimTeamId == teamId))
            .ToListAsync(ct);
        await RemoveAwdAttackScoreArtifactsAsync(db, competitionId, teamId, awdAttackRecords, ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdAttackRecords.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && (r.AttackerTeamId == teamId || r.VictimTeamId == teamId)), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdCheckResults.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdGameBoxes.IgnoreQueryFilters()
            .Where(g => g.CompetitionId == competitionId && g.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpTeamChallengeStates.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpRoundScores.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        var awdpPatchSubmissionIds = await db.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        await RemoveAwdpPatchTasksAsync(db, competitionId, teamId, awdpPatchSubmissionIds, ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.AwdpPatchSubmissions.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.TeamChallengeInstances.IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && i.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.KohControlRecords.IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.TeamId == teamId), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.CheatIncidents.IgnoreQueryFilters()
            .Where(i => i.CompetitionId == competitionId && (i.SuspectTeamId == teamId || i.VictimTeamId == teamId)), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.CompetitionLogs.IgnoreQueryFilters()
            .Where(l => l.CompetitionId == competitionId && l.TeamId == teamId), ct);

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
        var solveCounts = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId &&
                submission.TeamId != deletedTeamId &&
                submission.IsCorrect &&
                submission.PenetrationFlagId.HasValue &&
                flagIds.Contains(submission.PenetrationFlagId.Value))
            .Join(
                db.Teams.IgnoreQueryFilters().Where(team =>
                    team.CompetitionId == competitionId &&
                    team.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !team.IsBanned),
                submission => submission.TeamId,
                team => team.Id,
                (submission, _) => new
                {
                    FlagId = submission.PenetrationFlagId!.Value,
                    submission.TeamId
                })
            .GroupBy(solve => solve.FlagId)
            .Select(group => new
            {
                FlagId = group.Key,
                Count = group.Select(solve => solve.TeamId).Distinct().Count()
            })
            .ToDictionaryAsync(item => item.FlagId, item => item.Count, ct);

        foreach (var flag in flags)
            flag.SolvedCount = solveCounts.GetValueOrDefault(flag.Id);
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
        var removedIds = removedRecords.Select(record => record.Id).ToArray();
        var roundNumbers = removedRecords.Select(record => record.RoundNumber).Distinct().ToArray();
        var victimTeamIds = removedRecords.Select(record => record.VictimTeamId).Distinct().ToArray();
        var challengeIds = removedRecords.Select(record => record.ChallengeId).Distinct().ToArray();
        var remainingVictimAttackKeys = (await db.AwdAttackRecords
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(record =>
                    record.CompetitionId == competitionId &&
                    !removedIds.Contains(record.Id) &&
                    record.AttackerTeamId != deletedTeamId &&
                    roundNumbers.Contains(record.RoundNumber) &&
                    victimTeamIds.Contains(record.VictimTeamId) &&
                    challengeIds.Contains(record.ChallengeId))
                .Select(record => new
                {
                    record.RoundNumber,
                    record.VictimTeamId,
                    record.ChallengeId
                })
                .Distinct()
                .ToListAsync(ct))
            .Select(record => (record.RoundNumber, record.VictimTeamId, record.ChallengeId))
            .ToHashSet();

        foreach (var record in removedRecords)
        {
            signalKeys.Add($"awd:{record.RoundNumber}:{record.AttackerTeamId:N}:{record.VictimTeamId:N}:{record.ChallengeId:N}:attack");

            if (!remainingVictimAttackKeys.Contains((record.RoundNumber, record.VictimTeamId, record.ChallengeId)))
                signalKeys.Add($"awd:{record.RoundNumber}:{record.VictimTeamId:N}:{record.ChallengeId:N}:been-attacked");
        }

        var eventKeys = signalKeys.Select(key => $"round:{key}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreSignals.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && signalKeys.Contains(s.IdempotencyKey)), ct);
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.ScoreEvents.IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && eventKeys.Contains(s.IdempotencyKey)), ct);
    }

    private static async Task RemoveAwdpPatchTasksAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid teamId,
        IReadOnlyCollection<Guid> submissionIds,
        CancellationToken ct)
    {
        if (submissionIds.Count > 0)
        {
            var submissionIdText = submissionIds.Select(id => id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tasks = await db.BackgroundTasks
                .IgnoreQueryFilters()
                .Where(t => t.CompetitionId == competitionId && t.Type == "awdp.patch.validation")
                .ToListAsync(ct);
            db.BackgroundTasks.RemoveRange(tasks.Where(t =>
                submissionIdText.Any(id => t.PayloadJson.Contains(id, StringComparison.OrdinalIgnoreCase))));
        }

        var teamIdText = teamId.ToString();
        await DeleteCompetitionEndpoint.DeleteAsync(db, db.BackgroundTasks
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.Type == "awdp.container.cleanup" &&
                t.PayloadJson.Contains(teamIdText)), ct);
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
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
