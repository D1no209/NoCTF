using NoCTF.Infrastructure.Persistence;
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
using NoCTF.Infrastructure.Challenges;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdRoundCoordinator(
    NoCtfDbContext db,
    IAwdRoundConfigurationCatalog configurations,
    IPostCommitMessagePublisher outbox,
    TimeProvider timeProvider,
    TeamChallengeCriticalSection teamChallengeCriticalSection) : IAwdRoundCoordinator
{
    public AwdRoundCoordinator(
        NoCtfDbContext db,
        IAwdRoundConfigurationCatalog configurations,
        IPostCommitMessagePublisher outbox,
        TimeProvider timeProvider)
        : this(
            db,
            configurations,
            outbox,
            timeProvider,
            new TeamChallengeCriticalSection())
    { }

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
        var status = await CompetitionStateReader.ReadAsync(
            db,
            message.CompetitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return MessageExecutionOutcome.RejectedBusiness;

        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking(),
                target => target.Challenge.ChallengeId,
                template => template.Id,
                (target, template) => new { target.Challenge, target.Competition, Template = template })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Awd
            || !target.Challenge.IsPublished)
            return MessageExecutionOutcome.RejectedBusiness;
        var handledAt = timeProvider.GetUtcNow();
        var latest = await LoadLatestWindowAsync(message.CompetitionChallengeId, cancellationToken);
        if (latest.Conflict)
            return MessageExecutionOutcome.Conflict;
        var effectiveRuntime = await CompetitionEffectiveRuntimeReader.ReadAsync(
            db,
            message.CompetitionId,
            target.Competition.StartAt,
            target.Competition.EndAt,
            handledAt,
            cancellationToken);
        if (!IsCurrentGenerationWindow(
                message,
                (AwdCompetitionModeConfiguration)target.Competition.ModeConfiguration!,
                effectiveRuntime.Elapsed,
                target.Competition.EndAt,
                latest.Window,
                handledAt))
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                message.CompetitionId,
                message.CompetitionChallengeId,
                handledAt));
            await db.SaveChangesAsync(cancellationToken);
            await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
            return MessageExecutionOutcome.Superseded;
        }

        var teamIds = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == message.CompetitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .OrderBy(team => team.Id)
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
                && instance.ProviderReceipt != null)
            .Select(instance => new
            {
                TeamId = instance.TeamId!.Value,
                instance.Id,
                RunnerId = instance.RunnerId!
            })
            .ToListAsync(cancellationToken);
        var runtimeByTeam = runtimes.ToDictionary(instance => instance.TeamId);
        var template = AwdFlagTemplateResolver.Resolve(
            (AwdCompetitionModeConfiguration)target.Competition.ModeConfiguration!,
            (AwdCompetitionChallengeRules)target.Challenge.Rules!);
        var candidates = existingFlags.Select(flag => flag.Flag).ToHashSet(StringComparer.Ordinal);
        foreach (var teamId in missing)
        {
            using var teamLease = await teamChallengeCriticalSection.AcquireAsync(
                db,
                teamId,
                message.CompetitionChallengeId,
                cancellationToken);
            if (await db.ChallengeFlags.AsNoTracking().AnyAsync(flag =>
                    flag.CompetitionChallengeId == message.CompetitionChallengeId
                    && flag.TeamId == teamId
                    && flag.SpecificationKind == SpecificationKind.AwdRound
                    && flag.SpecificationId == message.Round.Value,
                    cancellationToken))
                continue;
            var context = new PerTeamFlagContext(
                target.Competition.FlagDerivationSecret,
                message.CompetitionId,
                target.Challenge.ChallengeId,
                message.CompetitionChallengeId,
                teamId,
                message.Round.Value);
            var flag = GenerateCandidate(template, context, candidates);
            var flagId = Guid.CreateVersion7();
            db.ChallengeFlags.Add(new AwdRoundChallengeFlag
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
                    message.ValidUntil,
                    runtime.RunnerId));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        // The Singular Agent rebuilds the next due round from these persisted flags.
        // Do not persist a recursive scheduled message as a second scheduling source.
        if (missing.Length == 0)
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
        var status = await CompetitionStateReader.ReadAsync(
            db,
            message.CompetitionId,
            cancellationToken);
        if (status != CompetitionStatus.Running)
            return MessageExecutionOutcome.RejectedBusiness;

        var target = await db.CompetitionChallenges.AsNoTracking()
            .Where(challenge => challenge.Id == message.CompetitionChallengeId
                && challenge.CompetitionId == message.CompetitionId
                && challenge.DeletedAt == null)
            .Join(
                db.Competitions.AsNoTracking(),
                challenge => challenge.CompetitionId,
                competition => competition.Id,
                (challenge, competition) => new { Challenge = challenge, Competition = competition })
            .Join(
                db.Challenges.AsNoTracking(),
                target => target.Challenge.ChallengeId,
                template => template.Id,
                (target, template) => new { target.Challenge, target.Competition, Template = template })
            .AsSplitQuery()
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null
            || target.Competition.Mode != GameMode.Awd
            || !target.Challenge.IsPublished)
            return MessageExecutionOutcome.RejectedBusiness;
        var hasEligibleTeam = await db.Teams.AsNoTracking().AnyAsync(
            team => team.CompetitionId == message.CompetitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null,
            cancellationToken);
        if (!hasEligibleTeam)
            return MessageExecutionOutcome.Idempotent;
        var latest = await LoadLatestWindowAsync(
            message.CompetitionChallengeId,
            cancellationToken);
        if (latest.Conflict)
            return MessageExecutionOutcome.Conflict;

        var handledAt = timeProvider.GetUtcNow();
        var settings = configurations.Get(
            (AwdCompetitionModeConfiguration)target.Competition.ModeConfiguration!);
        var effectiveRunningTime = (await CompetitionEffectiveRuntimeReader.ReadAsync(
            db,
            message.CompetitionId,
            target.Competition.StartAt,
            target.Competition.EndAt,
            handledAt,
            cancellationToken)).Elapsed;
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
                return MessageExecutionOutcome.DeferredSchedule;
            case AwdRoundPlanKind.Current:
                var current = plan.Window!.Value;
                if (current.ValidUntil >= target.Competition.EndAt)
                    return MessageExecutionOutcome.Idempotent;
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
                    window.ValidUntil));
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
        AwdCompetitionModeConfiguration competitionConfiguration,
        TimeSpan effectiveRunningTime,
        DateTimeOffset competitionEndAt,
        AwdPersistedRoundWindow? latest,
        DateTimeOffset handledAt)
    {
        var settings = configurations.Get(competitionConfiguration);
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

    private async Task CommitAndFlushIfOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
            return;
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushCommittedMessagesAsync();
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
