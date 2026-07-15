using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.BackgroundTasks;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class DeleteCompetitionEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    IContainerManager containerManager,
    ICompetitionExecutionLease executionLease,
    IRedisLeaderboardCache leaderboardCache) : Endpoint<EmptyRequest>, IAuditableEndpoint
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

        await using (var preparationLease = await executionLease.TryAcquireAsync(
            dbContext,
            CompetitionExecutionLeaseKeys.RuntimePreparation,
            id,
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
            competition.Status = NoCTF.Core.CompetitionStatus.Finished;
            competition.EndTime = DateTime.UtcNow < competition.EndTime ? DateTime.UtcNow : competition.EndTime;
            await dbContext.SaveChangesAsync(preparationCts.Token);
        }

        await leaderboardCache.InvalidateAsync(id, ct);

        await ContainerCleanupRuntime.CleanupCompetitionAsync(
            dbContext,
            containerManager,
            executionLease,
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
        var patchArchiveKeys = await dbContext.AwdpPatchSubmissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(submission => submission.CompetitionId == id)
            .Select(submission => submission.PatchArchiveUrl)
            .ToListAsync(ct);
        await using var cleanupTransaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(ct)
            : null;
        await DeleteCompetitionArtifactsAsync(dbContext, id, ct);
        dbContext.Competitions.Remove(competition);
        await StorageObjectCleanup.EnqueueAsync(
            dbContext,
            storageKeys
                .SelectMany(k => new[] { k.AttachmentStorageKey, k.PatchTemplateStorageKey })
                .Concat(patchArchiveKeys),
            ct);
        await dbContext.SaveChangesAsync(ct);
        if (cleanupTransaction is not null)
            await cleanupTransaction.CommitAsync(ct);
        await SendNoContentAsync(ct);
    }

    internal static async Task DeleteCompetitionArtifactsAsync(
        ApplicationDbContext db,
        Guid competitionId,
        CancellationToken ct)
    {
        await DeleteAsync(db, db.TeamMembers.Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.CompetitionCollaborators.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.ChallengeHints.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.Submissions.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.ScoreEvents.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.ScoreSignals.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.CtfDynamicFlags.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.DynamicFlagInstances.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.CompetitionLogs.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.CheatIncidents.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdAttackRecords.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdCheckResults.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdFlags.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdRounds.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdGameBoxes.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdpRoundScores.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdpRounds.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdpPatchSubmissions.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.AwdpTeamChallengeStates.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.PenetrationFlags.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.PenetrationNodes.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.TeamChallengeInstances.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.PenetrationTopologies.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.KohControlRecords.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.BackgroundTasks.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.Challenges.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
        await DeleteAsync(db, db.Teams.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId), ct);
    }

    internal static async Task DeleteAsync<TEntity>(
        ApplicationDbContext db,
        IQueryable<TEntity> query,
        CancellationToken ct)
        where TEntity : class
    {
        if (db.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(ct);
            return;
        }

        db.RemoveRange(await query.ToListAsync(ct));
    }
}
