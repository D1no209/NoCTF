using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.GameModes.Flags;

namespace NoCTF.Infrastructure.Persistence;

public sealed class PostgresAwdRoundCoordinator(
    NoCtfDbContext db,
    AwdRoundConfigurationCatalog configurations,
    ITransactionalMessageOutbox outbox,
    TimeProvider timeProvider) : IAwdRoundCoordinator
{
    public async Task<MessageExecutionOutcome> GenerateFlagsAsync(
        GenerateAwdFlags message,
        CancellationToken cancellationToken)
    {
        message = message with
        {
            ValidStart = ToPostgresTimestamp(message.ValidStart),
            ValidUntil = ToPostgresTimestamp(message.ValidUntil)
        };
        if (message.ValidStart >= message.ValidUntil)
            return MessageExecutionOutcome.RejectedBusiness;
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var status = await CompetitionWriteLock.AcquireAsync(
            db,
            message.CompetitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return MessageExecutionOutcome.RejectedBusiness;

        var target = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Awd
            || !target.Challenge.IsPublished)
            return MessageExecutionOutcome.RejectedBusiness;
        if (target.Competition.ConfigurationRevision != message.CompetitionConfigurationRevision
            || target.Challenge.Revision != message.ProcessingVersion)
            return MessageExecutionOutcome.Superseded;

        var handledAt = timeProvider.GetUtcNow();
        var latest = await LoadLatestWindowAsync(message.CompetitionChallengeId, cancellationToken);
        if (latest.Conflict)
            return MessageExecutionOutcome.Conflict;
        if (!IsCurrentGenerationWindow(
                message,
                target.Competition.ConfigurationJson,
                target.Competition.AccumulatedRunningSeconds,
                target.Competition.RunningSince,
                target.Competition.EndAt,
                latest.Window,
                handledAt))
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                message.CompetitionId,
                message.CompetitionChallengeId,
                handledAt,
                message.CompetitionConfigurationRevision,
                message.ProcessingVersion));
            await db.SaveChangesAsync(cancellationToken);
            await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
            return MessageExecutionOutcome.Superseded;
        }

        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == message.CompetitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .Select(team => team.Id)
            .ToListAsync(cancellationToken);
        var existingFlags = await db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.CompetitionChallengeId == message.CompetitionChallengeId
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.SpecificationId == message.Round.Value
                && flag.TeamId != null)
            .Select(flag => new { TeamId = flag.TeamId!.Value, flag.Flag })
            .ToListAsync(cancellationToken);
        var missing = teamIds.Except(existingFlags.Select(flag => flag.TeamId)).ToArray();
        var runtimes = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionChallengeId == message.CompetitionChallengeId
                && instance.TeamId != null
                && missing.Contains(instance.TeamId.Value)
                && instance.State == NoCTF.Domain.Runtime.RuntimeState.Running
                && instance.RunnerId != null
                && instance.ProviderReceiptJson != null)
            .Select(instance => new
            {
                TeamId = instance.TeamId!.Value,
                instance.Id,
                instance.Generation,
                instance.ProcessingVersion,
                instance.RunnerPool,
                RunnerId = instance.RunnerId!
            })
            .ToListAsync(cancellationToken);
        var runtimeByTeam = runtimes.ToDictionary(instance => instance.TeamId);
        var template = ResolveTemplate(
            target.Competition.ConfigurationJson,
            target.Challenge.ConfigurationJson);
        var candidates = existingFlags.Select(flag => flag.Flag).ToHashSet(StringComparer.Ordinal);
        foreach (var teamId in missing)
        {
            var context = new PerTeamFlagContext(
                target.Competition.FlagDerivationSecret,
                message.CompetitionId,
                target.Challenge.ChallengeId,
                message.CompetitionChallengeId,
                teamId);
            var flag = GenerateCandidate(template, context, candidates);
            var flagId = Guid.CreateVersion7();
            db.ChallengeFlags.Add(new ChallengeFlag
            {
                Id = flagId,
                CompetitionChallengeId = message.CompetitionChallengeId,
                TeamId = teamId,
                Flag = flag,
                FlagSha256 = ManageChallengeFlags.Hash(flag),
                SpecificationKind = SpecificationKind.AwdRound,
                SpecificationId = message.Round.Value,
                ValidStart = message.ValidStart,
                ValidUntil = message.ValidUntil,
                CreatedAt = handledAt
            });
            candidates.Add(flag);
            if (runtimeByTeam.TryGetValue(teamId, out var runtime))
            {
                await outbox.PublishToRunnerNodeAsync(new InjectAwdFlag(
                    runtime.Id,
                    message.CompetitionChallengeId,
                    flagId,
                    runtime.Generation,
                    runtime.ProcessingVersion,
                    message.ValidUntil,
                    runtime.RunnerPool,
                    runtime.RunnerId));
            }
        }
        var scheduledSuccessor = !ScheduleMatches(
            target.Challenge,
            message.Round.Round,
            message.ValidUntil,
            message.CompetitionConfigurationRevision,
            message.ProcessingVersion);
        if (scheduledSuccessor)
        {
            SetScheduleFence(
                target.Challenge,
                message.Round.Round,
                message.ValidUntil,
                message.CompetitionConfigurationRevision,
                message.ProcessingVersion);
            if (message.ValidUntil < target.Competition.EndAt)
            {
                await outbox.ScheduleAsync(
                    new AdvanceAwdRound(
                        message.CompetitionId,
                        message.CompetitionChallengeId,
                        message.ValidUntil,
                        message.CompetitionConfigurationRevision,
                        message.ProcessingVersion),
                    message.ValidUntil);
            }
        }
        if (missing.Length == 0 && !scheduledSuccessor)
            return MessageExecutionOutcome.Idempotent;
        await db.SaveChangesAsync(cancellationToken);
        await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
        return MessageExecutionOutcome.Applied;
    }

    public async Task<MessageExecutionOutcome> AdvanceAsync(
        AdvanceAwdRound message,
        CancellationToken cancellationToken)
    {
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var status = await CompetitionWriteLock.AcquireAsync(
            db,
            message.CompetitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return MessageExecutionOutcome.RejectedBusiness;

        var target = await db.CompetitionChallenges
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions,
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Awd
            || !target.Challenge.IsPublished)
            return MessageExecutionOutcome.RejectedBusiness;
        if (target.Competition.ConfigurationRevision != message.CompetitionConfigurationRevision
            || target.Challenge.Revision != message.ProcessingVersion)
            return MessageExecutionOutcome.Superseded;

        var latest = await LoadLatestWindowAsync(
            message.CompetitionChallengeId,
            cancellationToken);
        if (latest.Conflict)
            return MessageExecutionOutcome.Conflict;

        var handledAt = timeProvider.GetUtcNow();
        var settings = configurations.Get(target.Competition.ConfigurationJson);
        var effectiveRunningTime = CalculateEffectiveRunningTime(
            target.Competition.AccumulatedRunningSeconds,
            target.Competition.RunningSince,
            handledAt);
        var plan = AwdRoundScheduler.PlanCurrentRound(
            handledAt,
            target.Competition.EndAt,
            effectiveRunningTime,
            TimeSpan.FromSeconds(settings.HardeningDurationSeconds),
            TimeSpan.FromSeconds(settings.RoundDurationSeconds),
            latest.Window);

        switch (plan.Kind)
        {
            case AwdRoundPlanKind.WaitingForHardening:
                var dueAt = handledAt
                    + (TimeSpan.FromSeconds(settings.HardeningDurationSeconds)
                        - effectiveRunningTime);
                if (dueAt < target.Competition.EndAt)
                {
                    var scheduled = await ScheduleAdvanceIfChangedAsync(
                        target.Challenge,
                        round: 0,
                        dueAt,
                        message);
                    if (scheduled)
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
                    }
                }
                return MessageExecutionOutcome.DeferredSchedule;
            case AwdRoundPlanKind.Current:
                var current = plan.Window!.Value;
                if (current.ValidUntil >= target.Competition.EndAt)
                    return MessageExecutionOutcome.Idempotent;
                var replacementScheduled = await ScheduleAdvanceIfChangedAsync(
                    target.Challenge,
                    current.Round,
                    current.ValidUntil,
                    message);
                if (!replacementScheduled)
                    return MessageExecutionOutcome.Idempotent;
                await db.SaveChangesAsync(cancellationToken);
                await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
                return MessageExecutionOutcome.DeferredSchedule;
            case AwdRoundPlanKind.Finished:
                return MessageExecutionOutcome.RejectedBusiness;
            case AwdRoundPlanKind.Create:
                var window = ToPostgresTimestamp(plan.Window!.Value);
                await outbox.PublishAsync(new GenerateAwdFlags(
                    message.CompetitionId,
                    message.CompetitionChallengeId,
                    AwdRoundSpecificationId.FromRound(window.Round),
                    window.ValidStart,
                    window.ValidUntil,
                    message.CompetitionConfigurationRevision,
                    message.ProcessingVersion));
                if (latest.Window is not null)
                    await outbox.PublishAsync(new ProjectLeaderboard(message.CompetitionId));
                await db.SaveChangesAsync(cancellationToken);
                await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
                return MessageExecutionOutcome.Applied;
            default:
                throw new ArgumentOutOfRangeException(nameof(plan.Kind), plan.Kind, null);
        }
    }

    private async Task<LatestWindow> LoadLatestWindowAsync(
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var facts = await db.ChallengeFlags.AsNoTracking()
            .Where(flag => flag.CompetitionChallengeId == competitionChallengeId
                && flag.SpecificationKind == SpecificationKind.AwdRound
                && flag.SpecificationId != null
                && flag.ValidStart != null
                && flag.ValidUntil != null)
            .Select(flag => new
            {
                SpecificationId = flag.SpecificationId!.Value,
                ValidStart = flag.ValidStart!.Value,
                ValidUntil = flag.ValidUntil!.Value
            })
            .ToListAsync(cancellationToken);
        if (facts.Count == 0)
            return new(null, false);

        var parsed = facts.Select(fact => new
        {
            Round = AwdRoundSpecificationId.Parse(fact.SpecificationId).Round,
            fact.ValidStart,
            fact.ValidUntil
        }).ToArray();
        var latestRound = parsed.Max(fact => fact.Round);
        var windows = parsed
            .Where(fact => fact.Round == latestRound)
            .Select(fact => new { fact.ValidStart, fact.ValidUntil })
            .Distinct()
            .ToArray();
        return windows.Length != 1
            ? new(null, true)
            : new(new AwdPersistedRoundWindow(
                latestRound,
                windows[0].ValidStart,
                windows[0].ValidUntil), false);
    }

    private readonly record struct LatestWindow(
        AwdPersistedRoundWindow? Window,
        bool Conflict);

    private bool IsCurrentGenerationWindow(
        GenerateAwdFlags message,
        string competitionConfigurationJson,
        long accumulatedRunningSeconds,
        DateTimeOffset? runningSince,
        DateTimeOffset competitionEndAt,
        AwdPersistedRoundWindow? latest,
        DateTimeOffset handledAt)
    {
        var settings = configurations.Get(competitionConfigurationJson);
        var effectiveRunningTime = CalculateEffectiveRunningTime(
            accumulatedRunningSeconds,
            runningSince,
            handledAt);
        var plan = AwdRoundScheduler.PlanCurrentRound(
            handledAt,
            competitionEndAt,
            effectiveRunningTime,
            TimeSpan.FromSeconds(settings.HardeningDurationSeconds),
            TimeSpan.FromSeconds(settings.RoundDurationSeconds),
            latest);
        if (plan is not
            {
                Kind: AwdRoundPlanKind.Create or AwdRoundPlanKind.Current,
                Window: { } window
            })
            return false;
        window = ToPostgresTimestamp(window);
        return window.Round == message.Round.Round
            && window.ValidStart == message.ValidStart
            && window.ValidUntil == message.ValidUntil;
    }

    private static TimeSpan CalculateEffectiveRunningTime(
        long accumulatedRunningSeconds,
        DateTimeOffset? runningSince,
        DateTimeOffset now)
    {
        var effective = TimeSpan.FromSeconds(accumulatedRunningSeconds);
        if (runningSince is DateTimeOffset startedAt && now > startedAt)
            effective += now - startedAt;
        return effective;
    }

    private static PerTeamFlagTemplate ResolveTemplate(string competitionJson, string challengeJson)
    {
        var competition = AwdConfigurationUpgrader.ParseCompetition(competitionJson);
        var challenge = AwdConfigurationUpgrader.ParseChallenge(challengeJson);
        return challenge.FlagTemplate ?? competition.FlagTemplate ?? PerTeamFlagTemplate.Default;
    }

    private static string GenerateCandidate(
        PerTeamFlagTemplate template,
        PerTeamFlagContext context,
        IReadOnlySet<string> candidates)
    {
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            var candidate = PerTeamFlagGenerator.Generate(template, context);
            if (attempt == 10
                || !candidates.Contains(candidate))
                return candidate;
        }
        throw new InvalidOperationException("Flag candidate loop did not return a value.");
    }

    private async Task<bool> ScheduleAdvanceIfChangedAsync(
        CompetitionChallenge challenge,
        int round,
        DateTimeOffset dueAt,
        AdvanceAwdRound message)
    {
        dueAt = ToPostgresTimestamp(dueAt);
        if (ScheduleMatches(
                challenge,
                round,
                dueAt,
                message.CompetitionConfigurationRevision,
                message.ProcessingVersion))
            return false;
        SetScheduleFence(
            challenge,
            round,
            dueAt,
            message.CompetitionConfigurationRevision,
            message.ProcessingVersion);
        await outbox.ScheduleAsync(message with { At = dueAt }, dueAt);
        return true;
    }

    private async Task CommitAndFlushIfOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
            return;
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushOutgoingMessagesAsync();
    }

    private static bool ScheduleMatches(
        CompetitionChallenge challenge,
        int round,
        DateTimeOffset dueAt,
        int competitionRevision,
        long challengeRevision) =>
        challenge.LastScheduledAwdRound == round
        && challenge.AwdScheduleDueAt is { } scheduledAt
        && ToPostgresTimestamp(scheduledAt) == ToPostgresTimestamp(dueAt)
        && challenge.AwdScheduleCompetitionRevision == competitionRevision
        && challenge.AwdScheduleChallengeRevision == challengeRevision;

    private static void SetScheduleFence(
        CompetitionChallenge challenge,
        int round,
        DateTimeOffset dueAt,
        int competitionRevision,
        long challengeRevision)
    {
        challenge.LastScheduledAwdRound = round;
        challenge.AwdScheduleDueAt = ToPostgresTimestamp(dueAt);
        challenge.AwdScheduleCompetitionRevision = competitionRevision;
        challenge.AwdScheduleChallengeRevision = challengeRevision;
    }

    private static AwdPersistedRoundWindow ToPostgresTimestamp(
        AwdPersistedRoundWindow window) =>
        window with
        {
            ValidStart = ToPostgresTimestamp(window.ValidStart),
            ValidUntil = ToPostgresTimestamp(window.ValidUntil)
        };

    private static DateTimeOffset ToPostgresTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new(
            utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
    }
}
