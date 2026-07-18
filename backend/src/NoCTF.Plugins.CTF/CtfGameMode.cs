using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Events;
using NoCTF.Application.Scoring;
using NoCTF.Application.BackgroundTasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.CTF;

/// <summary>
/// CTF game mode: validates flags, prevents duplicate solves, records submissions,
/// emits neutral scoring signals, and fires SignalR notifications via ISubmissionEventHandler.
/// </summary>
public class CtfGameMode : IGameMode
{
    private readonly ApplicationDbContext _db;
    private readonly ISubmissionEventHandler _submissionEventHandler;
    private readonly IScoreSignalEmitter _scoreSignalEmitter;
    private readonly ICtfScoreRebuilder _scoreRebuilder;
    private readonly IChallengeSubmissionHandlerRegistry _challengeSubmissionHandlers;
    private readonly IBackgroundTaskQueue? _backgroundTasks;
    private readonly ILogger<CtfGameMode> _logger;
    private readonly ICompetitionExecutionLease _executionLease;

    public GameModeType Type => GameModeType.Ctf;

    public CtfGameMode(
        ApplicationDbContext db,
        ISubmissionEventHandler submissionEventHandler,
        IScoreSignalEmitter scoreSignalEmitter,
        ICtfScoreRebuilder scoreRebuilder,
        IChallengeSubmissionHandlerRegistry challengeSubmissionHandlers,
        IBackgroundTaskQueue? backgroundTasks = null,
        ILogger<CtfGameMode>? logger = null,
        ICompetitionExecutionLease? executionLease = null)
    {
        _db = db;
        _submissionEventHandler = submissionEventHandler;
        _scoreSignalEmitter = scoreSignalEmitter;
        _scoreRebuilder = scoreRebuilder;
        _challengeSubmissionHandlers = challengeSubmissionHandlers;
        _backgroundTasks = backgroundTasks;
        _logger = logger ?? NullLogger<CtfGameMode>.Instance;
        _executionLease = executionLease ?? new CompetitionExecutionLease();
    }

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public async Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        var challenge = await _db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.Id == context.ChallengeId &&
                c.CompetitionId == context.CompetitionId &&
                !c.IsDeleting,
                cancellationToken);

        if (challenge is null)
            return SubmissionResult.WrongFlag;

        var competition = await _db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, cancellationToken);

        if (competition is null)
            return SubmissionResult.WrongFlag;

        var now = DateTime.UtcNow;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return SubmissionResult.CompetitionNotStarted;
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return SubmissionResult.CompetitionEnded;
        if (competition.Status == CompetitionStatus.Paused)
            return SubmissionResult.CompetitionPaused;

        var handler = _challengeSubmissionHandlers.FindHandler(challenge.TypeId);
        if (handler is not null)
            return (await handler.ProcessSubmissionAsync(context, challenge, cancellationToken)).Result;

        // Validate flag using timing-safe comparison. New CTF challenges store only
        // the flag content; competition bindings provide the flag prefix.
        var flagSecret = challenge.FlagSecret ?? string.Empty;
        var isDynamicUuid = string.Equals(flagSecret.Trim(), "[UUID]", StringComparison.OrdinalIgnoreCase);
        var expectedFlag = await ResolveExpectedFlagAsync(challenge, context.TeamId, isDynamicUuid, cancellationToken);
        if (isDynamicUuid && expectedFlag is null)
            return SubmissionResult.InstanceRequired;

        var isCorrect = FlagValidator.IsMatch(context.FlagContent, expectedFlag ?? string.Empty);

        if (!isCorrect && !isDynamicUuid && LooksLikeLegacyFullFlag(flagSecret))
        {
            isCorrect = FlagValidator.IsMatch(context.FlagContent, flagSecret);
        }

        CtfDynamicFlag? stolenFlag = null;
        if (!isCorrect && isDynamicUuid)
        {
            stolenFlag = await FindStolenDynamicFlagAsync(challenge, context, cancellationToken);
        }

        // Return duplicate solves normally, but still record suspicious cross-team
        // dynamic flag submissions even after the team has already solved the task.
        var alreadySolved = await _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

        if (alreadySolved && stolenFlag is null)
            return SubmissionResult.AlreadySolved;

        Submission submission;
        bool isFirstBlood;
        var solveRank = 0;
        await using (var preparationLease = await SubmissionMutationGuard.TryAcquireAsync(
                         _executionLease,
                         _db,
                         context.CompetitionId,
                         cancellationToken))
        {
            if (preparationLease is null)
                return SubmissionResult.WrongFlag;

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                preparationLease.LostToken);
            var mutationCt = preparationCts.Token;
            var guard = await SubmissionMutationGuard.ValidateAsync(
                _db,
                context.CompetitionId,
                context.ChallengeId,
                [context.TeamId],
                DateTime.UtcNow,
                mutationCt,
                expectedGameMode: GameModeType.Ctf);
            if (!guard.IsAllowed)
                return guard.Rejection!.Value;
            challenge = guard.Challenge!;

            await using var transaction = await _db.Database.BeginTransactionAsync(mutationCt);

            var redactedSubmittedFlag = RedactSubmittedFlag(context.FlagContent);
            submission = new Submission
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                TeamId = context.TeamId,
                ChallengeId = context.ChallengeId,
                UserId = context.UserId,
                FlagContent = redactedSubmittedFlag,
                IsCorrect = isCorrect,
                SubmittedAt = DateTime.UtcNow,
                IpAddress = context.IpAddress
            };

            _db.Submissions.Add(submission);
            _db.CompetitionLogs.Add(new CompetitionLog
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                Level = isCorrect ? "info" : stolenFlag is null ? "warning" : "error",
                EventType = isCorrect ? "flag.accepted" : stolenFlag is null ? "flag.rejected" : "flag.suspected_cheat",
                Message = isCorrect
                    ? $"Team submitted a correct flag for challenge {challenge.Title}."
                    : stolenFlag is null
                        ? $"Team submitted a wrong flag for challenge {challenge.Title}."
                        : $"Team submitted another team's dynamic flag for challenge {challenge.Title}.",
                TeamId = context.TeamId,
                UserId = context.UserId,
                ChallengeId = context.ChallengeId,
                MetadataJson = ScoringJson.Serialize(new
                {
                    submittedFlag = context.FlagContent,
                    victimTeamId = stolenFlag?.TeamId
                }),
                CreatedAt = submission.SubmittedAt,
            });

            if (stolenFlag is not null)
            {
                _db.CheatIncidents.Add(new CheatIncident
                {
                    Id = Guid.NewGuid(),
                    CompetitionId = context.CompetitionId,
                    SuspectTeamId = context.TeamId,
                    VictimTeamId = stolenFlag.TeamId,
                    ChallengeId = context.ChallengeId,
                    UserId = context.UserId,
                    SubmittedFlag = redactedSubmittedFlag,
                    Reason = "submitted_other_team_dynamic_flag",
                    CreatedAt = submission.SubmittedAt,
                });
            }

            if (!isCorrect)
            {
                await _db.SaveChangesAsync(mutationCt);
                await transaction.CommitAsync(mutationCt);
                return SubmissionResult.WrongFlag;
            }

            await using (await CtfFirstBloodLock.AcquireAsync(
                             _db,
                             context.CompetitionId,
                             context.ChallengeId,
                             mutationCt))
            {
                // RuntimePreparation is always acquired before the challenge
                // lock/transaction boundary, matching destructive lifecycle
                // lock ordering and preventing cross-lock deadlocks.
                var solveCount = await _db.Submissions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .CountAsync(s =>
                        s.CompetitionId == context.CompetitionId &&
                        s.ChallengeId == context.ChallengeId &&
                        s.IsCorrect,
                        mutationCt);
                solveRank = solveCount + 1;
                isFirstBlood = solveRank == 1;

                try
                {
                    await _db.SaveChangesAsync(mutationCt);
                }
                catch (DbUpdateException)
                {
                    // PostgreSQL aborts the transaction after any database error,
                    // so it cannot be queried until the failed transaction has
                    // been rolled back. Clear the failed submission/log entities
                    // as well, otherwise the endpoint's later audit save retries
                    // those Added entries on the same scoped DbContext.
                    await transaction.RollbackAsync(CancellationToken.None);
                    _db.ChangeTracker.Clear();

                    if (await HasCorrectSolveAsync(context, mutationCt))
                        return SubmissionResult.AlreadySolved;

                    throw;
                }

                await _scoreSignalEmitter.PersistAsync(new ScoreSignalCreate(
                    CompetitionId: context.CompetitionId,
                    TeamId: context.TeamId,
                    SignalType: ScoreSignalTypes.SolveAccepted,
                    IdempotencyKey: $"ctf:{context.TeamId:N}:{context.ChallengeId:N}:solve",
                    SubjectType: "challenge",
                    SubjectId: context.ChallengeId,
                    ActorUserId: context.UserId,
                    PayloadJson: ScoringJson.Serialize(new { submissionId = submission.Id }),
                    OccurredAt: submission.SubmittedAt), mutationCt);

                await transaction.CommitAsync(mutationCt);
            }
        }
        try
        {
            await _scoreRebuilder.RebuildChallengeAsync(
                context.CompetitionId,
                context.ChallengeId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to schedule score rebuild for competition {CompetitionId}, challenge {ChallengeId}.",
                context.CompetitionId,
                context.ChallengeId);
            if (_backgroundTasks is not null)
            {
                try
                {
                    await _backgroundTasks.EnqueueAsync(
                        context.CompetitionId,
                        CtfScoreRebuildJobHandler.JobType,
                        new CtfScoreRebuildPayload(context.ChallengeId),
                        CancellationToken.None);
                }
                catch (Exception enqueueException)
                {
                    _logger.LogError(
                        enqueueException,
                        "Failed to enqueue fallback score rebuild for competition {CompetitionId}, challenge {ChallengeId}.",
                        context.CompetitionId,
                        context.ChallengeId);
                }
            }
        }

        // Load team name for notification
        var team = await _db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == context.TeamId, cancellationToken);

        var teamName = team?.Name ?? context.TeamId.ToString();

        var pointsAwarded = await _db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == context.CompetitionId &&
                e.TeamId == context.TeamId &&
                e.ChallengeId == context.ChallengeId &&
                e.ScoringKey == ScoringKeys.DecaySolve)
            .SumAsync(e => e.PointsDelta, cancellationToken);

        // Fire post-solve event: updates leaderboard cache + SignalR notifications
        try
        {
            await _submissionEventHandler.HandleAsync(new SubmissionSolvedEvent(
                CompetitionId: context.CompetitionId,
                ChallengeId: context.ChallengeId,
                ChallengeName: challenge.Title,
                TeamId: context.TeamId,
                TeamName: teamName,
                IsFirstBlood: isFirstBlood,
                PointsAwarded: pointsAwarded,
                SolveRank: solveRank,
                SubmissionId: submission.Id,
                UserId: context.UserId,
                BloodScopeId: context.ChallengeId,
                OccurredAt: submission.SubmittedAt), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Post-solve notification failed for submission {SubmissionId}; the accepted solve remains committed.",
                submission.Id);
            if (_backgroundTasks is not null)
            {
                try
                {
                    await _backgroundTasks.EnqueueAsync(
                        context.CompetitionId,
                        CtfScoreRebuildJobHandler.JobType,
                        new CtfScoreRebuildPayload(context.ChallengeId),
                        CancellationToken.None);
                }
                catch (Exception enqueueException)
                {
                    _logger.LogError(
                        enqueueException,
                        "Failed to enqueue leaderboard recovery for competition {CompetitionId}, challenge {ChallengeId}.",
                        context.CompetitionId,
                        context.ChallengeId);
                }
            }
        }

        return SubmissionResult.Accepted;
    }

    private Task<bool> HasCorrectSolveAsync(SubmissionContext context, CancellationToken cancellationToken)
        => _db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(s =>
                s.CompetitionId == context.CompetitionId &&
                s.TeamId == context.TeamId &&
                s.ChallengeId == context.ChallengeId &&
                s.IsCorrect,
                cancellationToken);

    private async Task<string?> ResolveExpectedFlagAsync(
        Challenge challenge,
        Guid teamId,
        bool isDynamicUuid,
        CancellationToken cancellationToken)
    {
        if (!isDynamicUuid)
            return FormatFlag(challenge, challenge.FlagSecret ?? string.Empty);

        var now = DateTime.UtcNow;
        var hasActiveInstance = await _db.AwdGameBoxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(g =>
                g.CompetitionId == challenge.CompetitionId &&
                g.TeamId == teamId &&
                g.ChallengeId == challenge.Id &&
                g.ContainerInstanceId != null &&
                (g.ExpiresAt == null || g.ExpiresAt > now),
                cancellationToken);
        if (!hasActiveInstance)
            return null;

        var flag = await _db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                f.CompetitionId == challenge.CompetitionId &&
                f.TeamId == teamId &&
                f.ChallengeId == challenge.Id,
                cancellationToken);

        return flag is null ? null : FormatFlag(challenge, flag.FlagUuid);
    }

    private async Task<CtfDynamicFlag?> FindStolenDynamicFlagAsync(
        Challenge challenge,
        SubmissionContext context,
        CancellationToken cancellationToken)
    {
        var candidates = SubmittedFlagCandidates(challenge, context.FlagContent)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return await _db.CtfDynamicFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f =>
                f.CompetitionId == context.CompetitionId &&
                f.ChallengeId == context.ChallengeId &&
                f.TeamId != context.TeamId &&
                candidates.Contains(f.FlagUuid))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IEnumerable<string> SubmittedFlagCandidates(Challenge challenge, string submittedFlag)
    {
        yield return submittedFlag;

        var configuredPrefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix)
            ? "flag"
            : challenge.FlagPrefix.Trim();
        if (configuredPrefix.Contains("{0}", StringComparison.Ordinal))
        {
            var parts = configuredPrefix.Split("{0}", StringSplitOptions.None);
            if (parts.Length == 2 &&
                submittedFlag.StartsWith(parts[0], StringComparison.Ordinal) &&
                submittedFlag.EndsWith(parts[1], StringComparison.Ordinal) &&
                submittedFlag.Length >= parts[0].Length + parts[1].Length)
            {
                yield return submittedFlag[parts[0].Length..^parts[1].Length];
            }
        }
        else if (configuredPrefix.Contains("{}", StringComparison.Ordinal))
        {
            var parts = configuredPrefix.Split("{}", StringSplitOptions.None);
            if (parts.Length == 2)
            {
                var before = $"{parts[0]}{{";
                var after = $"}}{parts[1]}";
                if (submittedFlag.StartsWith(before, StringComparison.Ordinal) &&
                    submittedFlag.EndsWith(after, StringComparison.Ordinal) &&
                    submittedFlag.Length >= before.Length + after.Length)
                {
                    yield return submittedFlag[before.Length..^after.Length];
                }
            }
        }
        else
        {
            var braceIndex = configuredPrefix.IndexOf('{', StringComparison.Ordinal);
            var prefix = braceIndex >= 0 ? configuredPrefix[..braceIndex].Trim() : configuredPrefix;
            var before = $"{prefix}{{";
            if (submittedFlag.StartsWith(before, StringComparison.Ordinal) &&
                submittedFlag.EndsWith("}", StringComparison.Ordinal) &&
                submittedFlag.Length > before.Length + 1)
            {
                yield return submittedFlag[before.Length..^1];
            }
        }
    }

    private static string FormatFlag(Challenge challenge, string content)
    {
        var prefix = string.IsNullOrWhiteSpace(challenge.FlagPrefix) ? "flag" : challenge.FlagPrefix.Trim();
        if (prefix.Contains("{0}", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, prefix, content);

        if (prefix.Contains("{}", StringComparison.Ordinal))
            return prefix.Replace("{}", $"{{{content}}}", StringComparison.Ordinal);

        var braceIndex = prefix.IndexOf('{', StringComparison.Ordinal);
        if (braceIndex >= 0)
            prefix = prefix[..braceIndex].Trim();

        return $"{prefix}{{{content}}}";
    }

    private static bool LooksLikeLegacyFullFlag(string value)
        => value.Contains('{', StringComparison.Ordinal) && value.EndsWith("}", StringComparison.Ordinal);

    private static string RedactSubmittedFlag(string submittedFlag)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(submittedFlag));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()};len:{submittedFlag.Length}";
    }
}

internal sealed class CtfFirstBloodLock : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<(Guid CompetitionId, Guid ChallengeId), LocalLockState> LocalLocks = new();
    private readonly (Guid CompetitionId, Guid ChallengeId) _localKey;
    private readonly LocalLockState? _localLock;
    private int _disposed;

    private CtfFirstBloodLock(
        (Guid CompetitionId, Guid ChallengeId) localKey,
        LocalLockState? localLock)
    {
        _localKey = localKey;
        _localLock = localLock;
    }

    public static async Task<CtfFirstBloodLock> AcquireAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            var lockKey = AdvisoryLockKey(competitionId, challengeId);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                ct);
            return new CtfFirstBloodLock(default, null);
        }

        var localKey = (competitionId, challengeId);
        while (true)
        {
            var state = LocalLocks.GetOrAdd(localKey, static _ => new LocalLockState());
            if (!state.TryAddReference())
            {
                LocalLocks.TryRemove(new KeyValuePair<(Guid, Guid), LocalLockState>(localKey, state));
                continue;
            }

            try
            {
                await state.Semaphore.WaitAsync(ct);
                return new CtfFirstBloodLock(localKey, state);
            }
            catch
            {
                ReleaseReference(localKey, state);
                throw;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_localLock is null || Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;

        _localLock.Semaphore.Release();
        ReleaseReference(_localKey, _localLock);
        return ValueTask.CompletedTask;
    }

    internal static bool HasLocalLock(Guid competitionId, Guid challengeId)
        => LocalLocks.ContainsKey((competitionId, challengeId));

    private static void ReleaseReference(
        (Guid CompetitionId, Guid ChallengeId) key,
        LocalLockState state)
    {
        if (state.ReleaseReference())
            LocalLocks.TryRemove(new KeyValuePair<(Guid, Guid), LocalLockState>(key, state));
    }

    private sealed class LocalLockState
    {
        private readonly object _sync = new();
        private int _references;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (_sync)
            {
                if (_retired)
                    return false;
                _references++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_sync)
            {
                _references--;
                if (_references != 0)
                    return false;
                _retired = true;
                return true;
            }
        }
    }

    private static long AdvisoryLockKey(Guid competitionId, Guid challengeId)
    {
        Span<byte> input = stackalloc byte[32];
        competitionId.TryWriteBytes(input);
        challengeId.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return BitConverter.ToInt64(hash);
    }
}
