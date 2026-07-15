using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.Plugins.AWD;

/// <summary>
/// AWD game mode: handles flag-stealing submissions and round notifications.
/// </summary>
public class AwdGameMode : IGameMode
{
    private readonly ApplicationDbContext _db;
    private readonly IHubNotifierService _hubNotifier;
    private readonly IScoreSignalEmitter _scoreSignalEmitter;
    private readonly ILogger<AwdGameMode> _logger;
    private readonly ICompetitionExecutionLease? _executionLease;

    public GameModeType Type => GameModeType.Awd;

    public AwdGameMode(
        ApplicationDbContext db,
        IHubNotifierService hubNotifier,
        IScoreSignalEmitter scoreSignalEmitter,
        ILogger<AwdGameMode>? logger = null,
        ICompetitionExecutionLease? executionLease = null)
    {
        _db = db;
        _hubNotifier = hubNotifier;
        _scoreSignalEmitter = scoreSignalEmitter;
        _logger = logger ?? NullLogger<AwdGameMode>.Instance;
        _executionLease = executionLease;
    }

    public Task InitializeAsync(GameContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Called by AwdRoundEngine on each round tick. Notifies clients that a new round started.
    /// </summary>
    public async Task OnRoundTickAsync(GameContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Configuration.TryGetValue("RoundNumber", out var roundStr) ||
            !int.TryParse(roundStr, out var roundNumber))
            return;

        await _hubNotifier.NotifyRoundStartedAsync(context.CompetitionId, roundNumber, cancellationToken);
    }

    /// <summary>
    /// Processes an AWD flag submission (flag stealing).
    /// Validates the flag, checks for self-attack and duplicates, records the attack, and emits a scoring signal.
    /// </summary>
    public async Task<SubmissionResult> ProcessSubmissionAsync(
        SubmissionContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var competition = await _db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == context.CompetitionId, cancellationToken);
        if (competition is null)
            return SubmissionResult.WrongFlag;
        if (now < competition.StartTime || competition.Status == CompetitionStatus.Draft)
            return SubmissionResult.CompetitionNotStarted;
        if (now > competition.EndTime || competition.Status == CompetitionStatus.Finished)
            return SubmissionResult.CompetitionEnded;
        if (competition.Status == CompetitionStatus.Paused)
            return SubmissionResult.CompetitionPaused;

        // Find the AwdFlag matching the submitted content for this competition
        var flag = await _db.AwdFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.CompetitionId == context.CompetitionId
                                   && f.ChallengeId == context.ChallengeId
                                   && f.FlagContent == context.FlagContent, cancellationToken);

        if (flag is null)
            return SubmissionResult.WrongFlag;

        // Self-attack prevention
        if (flag.TeamId == context.TeamId)
            return SubmissionResult.WrongFlag;

        var activeTeamIds = await _db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == context.CompetitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned &&
                (t.Id == context.TeamId || t.Id == flag.TeamId))
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        if (!activeTeamIds.Contains(context.TeamId) || !activeTeamIds.Contains(flag.TeamId))
            return SubmissionResult.WrongFlag;

        // Get the current (latest) round number
        var latestRound = await _db.AwdRounds
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == context.CompetitionId)
            .OrderByDescending(r => r.RoundNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (latestRound is null)
            return SubmissionResult.WrongFlag;

        var totalRounds = competition.TotalRounds ?? 10;
        if (latestRound.Status != AwdRoundStatus.Running || latestRound.RoundNumber > totalRounds)
            return SubmissionResult.CompetitionEnded;

        int currentRound = latestRound.RoundNumber;

        // Flag validity window check
        int validityRounds = competition.FlagValidityRounds ?? 2;
        int minValidRound = currentRound - validityRounds + 1;

        if (flag.RoundNumber < minValidRound || flag.RoundNumber > currentRound)
            return SubmissionResult.WrongFlag;

        AwdAttackRecord attack;
        var leaseProvider = _executionLease ?? new CompetitionExecutionLease();
        await using (var preparationLease = await SubmissionMutationGuard.TryAcquireAsync(
                         leaseProvider,
                         _db,
                         context.CompetitionId,
                         cancellationToken))
        {
            // Challenge deletion and competition teardown commit their
            // tombstone under the same short lease. Revalidate after acquiring
            // it so an already-read flag cannot recreate attack or score rows.
            if (preparationLease is null)
                return SubmissionResult.WrongFlag;

            using var preparationCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                preparationLease.LostToken);
            var mutationCt = preparationCts.Token;
            var mutationNow = DateTime.UtcNow;
            var guard = await SubmissionMutationGuard.ValidateAsync(
                _db,
                context.CompetitionId,
                context.ChallengeId,
                [context.TeamId, flag.TeamId],
                mutationNow,
                mutationCt,
                expectedGameMode: GameModeType.Awd);
            if (!guard.IsAllowed)
                return guard.Rejection!.Value;

            var guardedFlag = await _db.AwdFlags
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate =>
                    candidate.Id == flag.Id &&
                    candidate.CompetitionId == context.CompetitionId &&
                    candidate.ChallengeId == context.ChallengeId &&
                    candidate.TeamId == flag.TeamId &&
                    candidate.RoundNumber == flag.RoundNumber &&
                    candidate.FlagContent == context.FlagContent,
                    mutationCt);
            if (guardedFlag is null)
                return SubmissionResult.WrongFlag;
            flag = guardedFlag;

            var competitionWindow = await _db.Competitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(candidate =>
                    candidate.Id == context.CompetitionId &&
                    candidate.Status != CompetitionStatus.Finished)
                .Select(candidate => new
                {
                    TotalRounds = candidate.TotalRounds ?? 10,
                    FlagValidityRounds = candidate.FlagValidityRounds ?? 2
                })
                .FirstOrDefaultAsync(mutationCt);
            var guardedRound = await _db.AwdRounds
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(candidate => candidate.CompetitionId == context.CompetitionId)
                .OrderByDescending(candidate => candidate.RoundNumber)
                .FirstOrDefaultAsync(mutationCt);
            if (competitionWindow is null ||
                guardedRound is null ||
                guardedRound.Status != AwdRoundStatus.Running ||
                guardedRound.RoundNumber > competitionWindow.TotalRounds)
            {
                return SubmissionResult.CompetitionEnded;
            }

            var guardedMinValidRound = guardedRound.RoundNumber - competitionWindow.FlagValidityRounds + 1;
            if (flag.RoundNumber < guardedMinValidRound || flag.RoundNumber > guardedRound.RoundNumber)
                return SubmissionResult.WrongFlag;

            // Duplicate attack prevention: same attacker/victim/challenge/round
            var existingAttack = await FindAttackAsync(
                context.CompetitionId,
                context.TeamId,
                flag.TeamId,
                flag.ChallengeId,
                flag.RoundNumber,
                mutationCt);

            if (existingAttack is not null)
            {
                // A duplicate request is also a safe repair trigger for a commit
                // whose response was lost. Re-emitting deterministic signal keys
                // rebuilds any missing score events without awarding twice.
                await _scoreSignalEmitter.EmitBatchAsync(
                    CreateAttackSignals(context, flag, existingAttack.Timestamp),
                    mutationCt);
                return SubmissionResult.WrongFlag;
            }

            attack = new AwdAttackRecord
            {
                Id = Guid.NewGuid(),
                CompetitionId = context.CompetitionId,
                AttackerTeamId = context.TeamId,
                VictimTeamId = flag.TeamId,
                ChallengeId = flag.ChallengeId,
                RoundNumber = flag.RoundNumber,
                FlagContent = RedactSubmittedFlag(context.FlagContent),
                Timestamp = DateTime.UtcNow
            };
            var signals = CreateAttackSignals(context, flag, attack.Timestamp);

            // Persist the accepted attack, its durable score facts, and their score
            // projection in one database transaction. A scoring failure therefore
            // cannot leave a committed attack that will never be awarded.
            await using IDbContextTransaction? transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(mutationCt)
                : null;

            try
            {
                _db.AwdAttackRecords.Add(attack);
                await _db.SaveChangesAsync(mutationCt);
                await _scoreSignalEmitter.EmitBatchAsync(signals, mutationCt);
                if (transaction is not null)
                    await transaction.CommitAsync(mutationCt);
            }
            catch (DbUpdateException)
            {
                if (transaction is not null)
                {
                    await RollbackQuietlyAsync(transaction);
                    _db.ChangeTracker.Clear();
                }

                var concurrentAttack = await FindAttackAsync(
                    context.CompetitionId,
                    context.TeamId,
                    flag.TeamId,
                    flag.ChallengeId,
                    flag.RoundNumber,
                    mutationCt);
                if (concurrentAttack is null)
                    throw;

                await _scoreSignalEmitter.EmitBatchAsync(
                    CreateAttackSignals(context, flag, concurrentAttack.Timestamp),
                    mutationCt);
                return SubmissionResult.WrongFlag;
            }
            catch
            {
                if (transaction is not null)
                {
                    await RollbackQuietlyAsync(transaction);
                    _db.ChangeTracker.Clear();
                }
                throw;
            }
        }

        // Broadcast attack log via SignalR — look up names for the notification
        var teamNames = await _db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == context.TeamId || t.Id == flag.TeamId)
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);

        var challenge = await _db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.Id == flag.ChallengeId && !c.IsDeleting)
            .Select(c => c.Title)
            .FirstOrDefaultAsync(cancellationToken);

        try
        {
            await _hubNotifier.NotifyAttackLogAsync(
                context.CompetitionId,
                context.TeamId, teamNames.GetValueOrDefault(context.TeamId) ?? context.TeamId.ToString(),
                flag.TeamId, teamNames.GetValueOrDefault(flag.TeamId) ?? flag.TeamId.ToString(),
                flag.ChallengeId, challenge ?? flag.ChallengeId.ToString(),
                flag.RoundNumber,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Attack notification failed for accepted AWD attack {AttackId}; scoring remains committed.",
                attack.Id);
        }

        return SubmissionResult.Accepted;
    }

    private Task<AwdAttackRecord?> FindAttackAsync(
        Guid competitionId,
        Guid attackerTeamId,
        Guid victimTeamId,
        Guid challengeId,
        int roundNumber,
        CancellationToken ct)
        => _db.AwdAttackRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.CompetitionId == competitionId &&
                a.AttackerTeamId == attackerTeamId &&
                a.VictimTeamId == victimTeamId &&
                a.ChallengeId == challengeId &&
                a.RoundNumber == roundNumber,
                ct);

    private static ScoreSignalCreate[] CreateAttackSignals(
        SubmissionContext context,
        AwdFlag flag,
        DateTime occurredAt)
        =>
        [
            new ScoreSignalCreate(
                CompetitionId: context.CompetitionId,
                TeamId: context.TeamId,
                SignalType: ScoreSignalTypes.AttackAccepted,
                IdempotencyKey: $"awd:{flag.RoundNumber}:{context.TeamId:N}:{flag.TeamId:N}:{flag.ChallengeId:N}:attack",
                SubjectType: "challenge",
                SubjectId: flag.ChallengeId,
                ActorUserId: context.UserId,
                RoundNumber: flag.RoundNumber,
                PayloadJson: ScoringJson.Serialize(new { victimTeamId = flag.TeamId }),
                OccurredAt: occurredAt),
            new ScoreSignalCreate(
                CompetitionId: context.CompetitionId,
                TeamId: flag.TeamId,
                SignalType: ScoreSignalTypes.ServiceAttacked,
                IdempotencyKey: $"awd:{flag.RoundNumber}:{flag.TeamId:N}:{flag.ChallengeId:N}:been-attacked",
                SubjectType: "challenge",
                SubjectId: flag.ChallengeId,
                ActorUserId: context.UserId,
                RoundNumber: flag.RoundNumber,
                PayloadJson: ScoringJson.Serialize(new { attackerTeamId = context.TeamId }),
                OccurredAt: occurredAt)
        ];

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            // Rollback must remain available after the request or lease-loss
            // token has been cancelled; transaction disposal and lease release
            // must not be masked by an already-cancelled mutation token.
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The transaction may already have completed after an ambiguous
            // commit. The deterministic duplicate repair below establishes the
            // final score projection either way.
        }
    }

    private static string RedactSubmittedFlag(string submittedFlag)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(submittedFlag));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()};len:{submittedFlag.Length}";
    }
}
