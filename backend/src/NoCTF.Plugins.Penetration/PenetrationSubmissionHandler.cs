using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.BackgroundTasks;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace NoCTF.Plugins.Penetration;

public class PenetrationSubmissionHandler(
    ApplicationDbContext db,
    PenetrationFlagService flagService,
    IScoreSignalEmitter scoreSignalEmitter,
    ISubmissionEventHandler submissionEventHandler,
    IServiceProvider serviceProvider,
    IBackgroundTaskQueue? backgroundTasks = null,
    ILogger<PenetrationSubmissionHandler>? logger = null,
    ICompetitionExecutionLease? executionLease = null)
    : IChallengeSubmissionHandler
{
    public string TypeId => PenetrationConstants.TypeId;

    public async Task<ChallengeSubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        Challenge challenge,
        CancellationToken ct = default)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, ct);
        if (competition is null) return new ChallengeSubmissionResult(SubmissionResult.WrongFlag);

        var now = DateTime.UtcNow;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionNotStarted);
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionEnded);
        if (competition.Status == CompetitionStatus.Paused)
            return new ChallengeSubmissionResult(SubmissionResult.CompetitionPaused);

        var instance = await db.TeamChallengeInstances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(i =>
                i.CompetitionId == context.CompetitionId &&
                i.TeamId == context.TeamId &&
                i.ChallengeId == context.ChallengeId,
                ct);

        if (instance is null || instance.Status != PenetrationInstanceStatus.Running)
            return new ChallengeSubmissionResult(SubmissionResult.InstanceRequired);
        if (instance.ExpiresAt is not null && instance.ExpiresAt <= now)
            return new ChallengeSubmissionResult(SubmissionResult.InstanceExpired);

        PenetrationFlag flag;
        Submission submission;
        bool isFirstBlood;
        var solveRank = 0;
        var leaseProvider = executionLease ?? new CompetitionExecutionLease();
        await using (var preparationLease = await SubmissionMutationGuard.TryAcquireAsync(
                         leaseProvider,
                         db,
                         context.CompetitionId,
                         ct))
        {
            if (preparationLease is null)
                return new ChallengeSubmissionResult(SubmissionResult.WrongFlag);

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                preparationLease.LostToken);
            var mutationCt = preparationCts.Token;
            now = DateTime.UtcNow;
            var guard = await SubmissionMutationGuard.ValidateAsync(
                db,
                context.CompetitionId,
                context.ChallengeId,
                [context.TeamId],
                now,
                mutationCt,
                expectedChallengeType: PenetrationConstants.TypeId);
            if (!guard.IsAllowed)
                return new ChallengeSubmissionResult(guard.Rejection!.Value);
            challenge = guard.Challenge!;

            instance = await db.TeamChallengeInstances
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(i =>
                    i.CompetitionId == context.CompetitionId &&
                    i.TeamId == context.TeamId &&
                    i.ChallengeId == context.ChallengeId,
                    mutationCt);
            if (instance is null || instance.Status != PenetrationInstanceStatus.Running)
                return new ChallengeSubmissionResult(SubmissionResult.InstanceRequired);
            if (instance.ExpiresAt is not null && instance.ExpiresAt <= now)
                return new ChallengeSubmissionResult(SubmissionResult.InstanceExpired);

            var match = await flagService.MatchAsync(challenge, instance, context.FlagContent, mutationCt);
            if (!match.IsCorrect)
            {
                if (await IsRateLimitedAsync(context, now, mutationCt))
                {
                    AddCompetitionLog(
                        context,
                        "penetration.flag.rate_limited",
                        "Team submitted penetration flags too frequently.",
                        "warning",
                        new
                        {
                            submittedFlag = context.FlagContent,
                            submitted = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent)
                        });
                    await db.SaveChangesAsync(mutationCt);
                    return new ChallengeSubmissionResult(SubmissionResult.FlagRateLimited);
                }

                await RecordRejectedSubmissionAsync(context, challenge, match, now, mutationCt);
                return new ChallengeSubmissionResult(SubmissionResult.WrongFlag);
            }

            flag = match.Flag!;
            var alreadySolved = await HasCorrectStageSolveAsync(context, flag.Id, mutationCt);
            if (alreadySolved)
                return new ChallengeSubmissionResult(SubmissionResult.AlreadySolved, BuildResultData(flag, true));

            await using var transaction = await db.Database.BeginTransactionAsync(mutationCt);
            await using var bloodLock = await PenetrationBloodRankLock.AcquireAsync(
                db, context.CompetitionId, flag.Id, mutationCt);
            var solveCount = await db.Submissions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(item =>
                    item.CompetitionId == context.CompetitionId &&
                    item.PenetrationFlagId == flag.Id &&
                    item.IsCorrect,
                    mutationCt);
            solveRank = solveCount + 1;
            isFirstBlood = solveRank == 1;
            submission = new Submission
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                TeamId = context.TeamId,
                ChallengeId = context.ChallengeId,
                PenetrationFlagId = flag.Id,
                UserId = context.UserId,
                FlagContent = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
                IsCorrect = true,
                SubmittedAt = now,
                IpAddress = context.IpAddress
            };

            db.Submissions.Add(submission);
            AddCompetitionLog(
                context,
                "penetration.flag.accepted",
                $"Team solved penetration stage {flag.Stage} for challenge {challenge.Title}.",
                "info",
                new
                {
                    submittedFlag = context.FlagContent,
                    flagId = flag.Id,
                    flag.Stage,
                    flagName = flag.Name,
                    submissionId = submission.Id
                });
            await flagService.MarkSolvedAsync(
                context.CompetitionId,
                context.TeamId,
                context.ChallengeId,
                flag.Id,
                now,
                mutationCt);

            try
            {
                await db.SaveChangesAsync(mutationCt);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                if (await HasCorrectStageSolveAsync(context, flag.Id, mutationCt))
                    return new ChallengeSubmissionResult(SubmissionResult.AlreadySolved, BuildResultData(flag, true));
                throw;
            }

            await scoreSignalEmitter.PersistAsync(new ScoreSignalCreate(
                CompetitionId: context.CompetitionId,
                TeamId: context.TeamId,
                SignalType: ScoreSignalTypes.PenetrationFlagAccepted,
                IdempotencyKey: $"penetration:{context.TeamId:N}:{context.ChallengeId:N}:{flag.Id:N}",
                SubjectType: "penetration-flag",
                SubjectId: flag.Id,
                ActorUserId: context.UserId,
                PayloadJson: ScoringJson.Serialize(new
                {
                    submissionId = submission.Id,
                    challengeId = context.ChallengeId,
                    flagId = flag.Id,
                    flag.Stage,
                    flagName = flag.Name,
                    score = flag.Score
                }),
                OccurredAt: now), mutationCt);

            await transaction.CommitAsync(mutationCt);
        }

        try
        {
            var scoreRebuilder = serviceProvider.GetRequiredService<ICtfScoreRebuilder>();
            await scoreRebuilder.RebuildChallengeAsync(
                context.CompetitionId,
                context.ChallengeId,
                ct);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<PenetrationSubmissionHandler>.Instance).LogError(
                ex,
                "Failed to rebuild penetration scores for competition {CompetitionId}, challenge {ChallengeId}.",
                context.CompetitionId,
                context.ChallengeId);
            if (backgroundTasks is not null)
            {
                try
                {
                    await backgroundTasks.EnqueueAsync(
                        context.CompetitionId,
                        CtfScoreRebuildJobHandler.JobType,
                        new CtfScoreRebuildPayload(context.ChallengeId),
                        CancellationToken.None);
                }
                catch (Exception enqueueException)
                {
                    (logger ?? NullLogger<PenetrationSubmissionHandler>.Instance).LogError(
                        enqueueException,
                        "Failed to enqueue fallback penetration score rebuild for competition {CompetitionId}, challenge {ChallengeId}.",
                        context.CompetitionId,
                        context.ChallengeId);
                }
            }
        }

        var team = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == context.TeamId, ct);
        var awarded = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == context.CompetitionId &&
                e.TeamId == context.TeamId &&
                e.ChallengeId == context.ChallengeId &&
                (e.ScoringKey == ScoringKeys.PenetrationStage ||
                 e.ScoringKey == ScoringKeys.PenetrationBloodBonus))
            .SumAsync(e => e.PointsDelta, ct);

        try
        {
            await submissionEventHandler.HandleAsync(new SubmissionSolvedEvent(
                CompetitionId: context.CompetitionId,
                ChallengeId: context.ChallengeId,
                ChallengeName: challenge.Title,
                TeamId: context.TeamId,
                TeamName: team?.Name ?? context.TeamId.ToString(),
                IsFirstBlood: isFirstBlood,
                PointsAwarded: awarded,
                SolveRank: solveRank,
                SubmissionId: submission.Id,
                UserId: context.UserId,
                BloodScopeId: flag.Id,
                OccurredAt: submission.SubmittedAt), ct);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<PenetrationSubmissionHandler>.Instance).LogError(
                ex,
                "Post-solve notification failed for penetration submission {SubmissionId}; the accepted stage remains committed.",
                submission.Id);
        }

        return new ChallengeSubmissionResult(SubmissionResult.Accepted, BuildResultData(flag, false));
    }

    private async Task RecordRejectedSubmissionAsync(
        SubmissionContext context,
        Challenge challenge,
        PenetrationFlagMatch match,
        DateTime now,
        CancellationToken ct)
    {
        db.Submissions.Add(new Submission
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            TeamId = context.TeamId,
            ChallengeId = context.ChallengeId,
            UserId = context.UserId,
            FlagContent = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
            IsCorrect = false,
            SubmittedAt = now,
            IpAddress = context.IpAddress
        });

        AddCompetitionLog(
            context,
            match.IsCrossTeamDynamicFlag ? "penetration.flag.suspected_cross_team" : "penetration.flag.rejected",
            match.IsCrossTeamDynamicFlag
                ? $"Team submitted another team's penetration dynamic flag for challenge {challenge.Title}."
                : $"Team submitted a wrong penetration flag for challenge {challenge.Title}.",
            match.IsCrossTeamDynamicFlag ? "error" : "warning",
            new
            {
                submittedFlag = context.FlagContent,
                submitted = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
                victimTeamId = match.VictimTeamId,
                flagId = match.Flag?.Id
            });

        if (match.IsCrossTeamDynamicFlag && match.VictimTeamId.HasValue)
        {
            db.CheatIncidents.Add(new CheatIncident
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                SuspectTeamId = context.TeamId,
                VictimTeamId = match.VictimTeamId.Value,
                ChallengeId = context.ChallengeId,
                UserId = context.UserId,
                SubmittedFlag = PenetrationFlagService.RedactSubmittedFlag(context.FlagContent),
                Reason = "submitted_other_team_penetration_dynamic_flag",
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private Task<bool> HasCorrectStageSolveAsync(SubmissionContext context, Guid flagId, CancellationToken ct)
        => db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.PenetrationFlagId == flagId &&
                s.IsCorrect,
                ct);

    private async Task<bool> IsRateLimitedAsync(SubmissionContext context, DateTime now, CancellationToken ct)
    {
        var oneMinuteAgo = now.AddMinutes(-1);
        var stats = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                !s.IsCorrect &&
                s.SubmittedAt >= oneMinuteAgo)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Latest = group.Max(s => s.SubmittedAt)
            })
            .FirstOrDefaultAsync(ct);

        return stats is not null &&
               (stats.Latest >= now.AddSeconds(-5) || stats.Count >= 10);
    }

    private void AddCompetitionLog(
        SubmissionContext context,
        string eventType,
        string message,
        string level,
        object? metadata)
    {
        db.CompetitionLogs.Add(new CompetitionLog
        {
            Id = Guid.NewGuid(),
            CompetitionId = context.CompetitionId,
            Level = level,
            EventType = eventType,
            Message = message,
            TeamId = context.TeamId,
            UserId = context.UserId,
            ChallengeId = context.ChallengeId,
            MetadataJson = metadata is null ? "{}" : ScoringJson.Serialize(metadata),
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static object BuildResultData(PenetrationFlag flag, bool alreadySolved)
        => new
        {
            flagId = flag.Id,
            flag.Stage,
            flag.Name,
            flag.Score,
            alreadySolved
        };
}

internal sealed class PenetrationBloodRankLock : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<(Guid CompetitionId, Guid FlagId), SemaphoreSlim> LocalLocks = new();
    private readonly SemaphoreSlim? _local;
    private readonly (Guid CompetitionId, Guid FlagId) _key;

    private PenetrationBloodRankLock(SemaphoreSlim? local, (Guid, Guid) key)
    {
        _local = local;
        _key = key;
    }

    public static async Task<PenetrationBloodRankLock> AcquireAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid flagId,
        CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var input = new byte[32];
            competitionId.TryWriteBytes(input);
            flagId.TryWriteBytes(input.AsSpan(16));
            var key = BitConverter.ToInt64(SHA256.HashData(input));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            return new PenetrationBloodRankLock(null, default);
        }

        var localKey = (competitionId, flagId);
        var semaphore = LocalLocks.GetOrAdd(localKey, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new PenetrationBloodRankLock(semaphore, localKey);
    }

    public ValueTask DisposeAsync()
    {
        if (_local is null) return ValueTask.CompletedTask;
        _local.Release();
        if (_local.CurrentCount == 1)
            LocalLocks.TryRemove(new KeyValuePair<(Guid, Guid), SemaphoreSlim>(_key, _local));
        return ValueTask.CompletedTask;
    }
}
