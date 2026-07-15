using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Plugins.KoH;

/// <summary>
/// Handles KoH control-record tracking and emits control-held scoring signals.
/// Extracted for testability.
/// </summary>
public class KohScoreEngine(
    ApplicationDbContext db,
    ILeaderboardService leaderboard,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    IScoreSignalEmitter scoreSignalEmitter,
    ICompetitionExecutionLease? executionLease = null,
    ILogger<KohScoreEngine>? logger = null)
{
    private readonly ILogger<KohScoreEngine> _logger = logger ?? NullLogger<KohScoreEngine>.Instance;

    internal sealed record ControlUpdate(Guid ChallengeId, Guid? ControllerTeamId);

    /// <summary>
    /// Updates the control record for a challenge based on the current controller.
    /// Emits a scoring signal if the same team holds control, or transitions to a new controller.
    /// </summary>
    public async Task UpdateControlAsync(
        Guid competitionId,
        Guid challengeId,
        Guid? newControllerTeamId,
        DateTime now,
        int controlPointsPerInterval,
        CancellationToken ct = default,
        bool refreshLeaderboard = true,
        bool validateController = true)
    {
        await UpdateControlsAsync(
            competitionId,
            [new ControlUpdate(challengeId, newControllerTeamId)],
            now,
            controlPointsPerInterval,
            ct,
            refreshLeaderboard,
            validateController);
    }

    /// <summary>
    /// Applies one poll's authoritative hill states under a single lifecycle
    /// lease. This turns the former per-hill query/save loop into a fixed number
    /// of set-oriented database operations while retaining the single-update API.
    /// </summary>
    internal async Task<bool> UpdateControlsAsync(
        Guid competitionId,
        IReadOnlyCollection<ControlUpdate> updates,
        DateTime now,
        int controlPointsPerInterval,
        CancellationToken ct = default,
        bool refreshLeaderboard = true,
        bool validateControllers = true)
    {
        if (updates.Count == 0)
            return true;

        var requestedUpdates = updates
            .GroupBy(update => update.ChallengeId)
            .Select(group => group.Last())
            .ToList();
        var shouldRefreshLeaderboard = false;
        var controllerChanges = new List<ControlUpdate>();
        var leaseProvider = executionLease ?? new CompetitionExecutionLease();
        await using (var preparationLease = await leaseProvider.TryAcquireAsync(
                         db,
                         CompetitionExecutionLeaseKeys.RuntimePreparation,
                         competitionId,
                         ct))
        {
            if (preparationLease is null)
                return false;

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            var mutationCt = preparationCts.Token;
            var validationNow = DateTime.UtcNow;
            var requestedChallengeIds = requestedUpdates
                .Select(update => update.ChallengeId)
                .ToArray();

            // Polling performs network I/O before reaching this method. The
            // challenges can therefore be tombstoned after targets were loaded.
            // Revalidate the whole set while holding the same short lease used
            // by destructive lifecycle operations.
            var validChallengeIds = (await db.Challenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(challenge =>
                    challenge.CompetitionId == competitionId &&
                    requestedChallengeIds.Contains(challenge.Id) &&
                    !challenge.IsDeleting)
                .Join(
                    db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(competition =>
                        competition.Id == competitionId &&
                        competition.Status == CompetitionStatus.Running &&
                        competition.StartTime <= validationNow &&
                        (competition.EndTime > validationNow ||
                         (competition.StartTime <= now && competition.EndTime > now))),
                    challenge => challenge.CompetitionId,
                    competition => competition.Id,
                    (challenge, _) => challenge.Id)
                .ToListAsync(mutationCt))
                .ToHashSet();
            if (validChallengeIds.Count == 0)
                return true;

            HashSet<Guid>? validControllerIds = null;
            if (validateControllers)
            {
                var requestedControllerIds = requestedUpdates
                    .Where(update => update.ControllerTeamId.HasValue)
                    .Select(update => update.ControllerTeamId!.Value)
                    .Distinct()
                    .ToArray();
                validControllerIds = (await db.Teams
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(t =>
                        requestedControllerIds.Contains(t.Id) &&
                        t.CompetitionId == competitionId &&
                        t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                        !t.IsBanned)
                    .Select(team => team.Id)
                    .ToListAsync(mutationCt))
                    .ToHashSet();
            }

            var activeRecords = await db.KohControlRecords
                .IgnoreQueryFilters()
                .Where(r => r.CompetitionId == competitionId
                         && validChallengeIds.Contains(r.ChallengeId)
                         && r.EndTime == null)
                .ToListAsync(mutationCt);
            var activeRecordMap = activeRecords
                .GroupBy(record => record.ChallengeId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(record => record.StartTime).First());
            var scoreSignals = new List<ScoreSignalCreate>();
            var newControlRecords = new List<KohControlRecord>();
            var endedControlRecord = false;

            foreach (var update in requestedUpdates.Where(update => validChallengeIds.Contains(update.ChallengeId)))
            {
                var newControllerTeamId = update.ControllerTeamId;
                if (newControllerTeamId.HasValue &&
                    validControllerIds is not null &&
                    !validControllerIds.Contains(newControllerTeamId.Value))
                {
                    newControllerTeamId = null;
                }

                activeRecordMap.TryGetValue(update.ChallengeId, out var activeRecord);
                if (activeRecord is not null && activeRecord.TeamId == newControllerTeamId)
                {
                    // A retry of the same logical poll can observe a transition
                    // that committed before the worker crashed. Do not turn that
                    // capture into a held-control award on replay.
                    if (newControllerTeamId.HasValue && !SamePollTimestamp(activeRecord.StartTime, now))
                    {
                        scoreSignals.Add(new ScoreSignalCreate(
                        CompetitionId: competitionId,
                        TeamId: newControllerTeamId.Value,
                        SignalType: ScoreSignalTypes.ControlHeld,
                        IdempotencyKey: $"koh:{update.ChallengeId:N}:{newControllerTeamId.Value:N}:{now:yyyyMMddHHmmss}",
                        SubjectType: "challenge",
                        SubjectId: update.ChallengeId,
                        PayloadJson: ScoringJson.Serialize(new
                        {
                            source = "poll",
                            points = controlPointsPerInterval
                        }),
                        OccurredAt: now));
                        shouldRefreshLeaderboard = true;
                    }
                    continue;
                }

                if (activeRecord is null && !newControllerTeamId.HasValue)
                    continue;

                // Controller changed — end old record, start new one
                if (activeRecord is not null)
                {
                    activeRecord.EndTime = now;
                    endedControlRecord = true;
                }

                if (newControllerTeamId.HasValue)
                {
                    newControlRecords.Add(new KohControlRecord
                    {
                        Id = Guid.NewGuid(),
                        CompetitionId = competitionId,
                        ChallengeId = update.ChallengeId,
                        TeamId = newControllerTeamId.Value,
                        StartTime = now,
                        EndTime = null
                    });
                }

                controllerChanges.Add(new ControlUpdate(update.ChallengeId, newControllerTeamId));
                shouldRefreshLeaderboard = true;
            }

            // End old windows before inserting replacements so PostgreSQL's
            // partial unique active-hill index never observes both rows active
            // in the same command batch. A crash between the two saves is safe:
            // replaying the same logical poll recreates the missing active row.
            if (endedControlRecord)
                await db.SaveChangesAsync(mutationCt);
            if (newControlRecords.Count > 0)
            {
                db.KohControlRecords.AddRange(newControlRecords);
                await db.SaveChangesAsync(mutationCt);
            }
            if (scoreSignals.Count > 0)
                await scoreSignalEmitter.EmitBatchAsync(scoreSignals, mutationCt);
        }

        // Notifications and the expensive leaderboard rebuild do not mutate
        // challenge-owned runtime facts, so they intentionally run after the
        // short lifecycle barrier has been released.
        foreach (var change in controllerChanges)
        {
            try
            {
                await hubNotifier.NotifyKohUpdateAsync(
                    competitionId,
                    change.ChallengeId,
                    change.ControllerTeamId,
                    now,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish KoH controller update for challenge {ChallengeId}.",
                    change.ChallengeId);
            }
        }

        if (refreshLeaderboard && shouldRefreshLeaderboard)
            await RefreshLeaderboardAsync(competitionId, ct);
        return true;
    }

    private static bool SamePollTimestamp(DateTime left, DateTime right)
        => Math.Abs((left - right).Ticks) < TimeSpan.TicksPerMillisecond;

    public async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct = default)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
        var entries = await leaderboard.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
    }
}
