using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NoCTF.Application.Competitions.Awd;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
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
        var round = await db.AwdRounds.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CompetitionId == message.CompetitionId
                && item.Number == message.Round.Round, cancellationToken);
        if (round is null || round.Missed || handledAt >= target.Competition.EndAt
            || handledAt < round.StartsAt || handledAt >= round.EndsAt
            || round.StartsAt != message.ValidStart || round.EndsAt != message.ValidUntil)
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                message.CompetitionId,
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
        // The Singular Agent rebuilds due work from independent persisted competition rounds.
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
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        var competition = await db.Competitions.AsSplitQuery().SingleOrDefaultAsync(
            item => item.Id == message.CompetitionId && item.DeletedAt == null, cancellationToken);
        if (competition is not { Mode: GameMode.Awd, Status: CompetitionStatus.Running }
            || competition.ModeConfiguration is not AwdCompetitionModeConfiguration configuration)
            return MessageExecutionOutcome.RejectedBusiness;
        var now = timeProvider.GetUtcNow();
        if (now >= competition.EndAt)
            return MessageExecutionOutcome.RejectedBusiness;
        var clock = await AwdRunningClock.ReadAsync(db, competition.Id, competition.StartAt, now, cancellationToken);
        var elapsed = clock.ElapsedAt(now);
        var rounds = await db.AwdRounds.Where(round => round.CompetitionId == competition.Id)
            .OrderBy(round => round.Number).ToListAsync(cancellationToken);
        var settings = configurations.Get(configuration);
        var plan = AwdRoundTimeline.Plan(rounds, elapsed,
            TimeSpan.FromSeconds(settings.HardeningDurationSeconds),
            TimeSpan.FromSeconds(settings.RoundDurationSeconds), configuration.AwdOptions.RoundGapSeconds,
            configuration.AwdOptions.TotalRounds);
        foreach (var planned in plan.NewRounds)
        {
            var endsAt = clock.At(TimeSpan.FromTicks(planned.RunningEndTicks), intervalStart: false);
            var partial = endsAt > competition.EndAt;
            var round = new AwdRound
            {
                Id = Guid.CreateVersion7(), CompetitionId = competition.Id, Number = planned.Number,
                StartsAt = clock.At(TimeSpan.FromTicks(planned.RunningStartTicks), intervalStart: true),
                EndsAt = partial ? competition.EndAt : endsAt,
                ActiveStartTicks = planned.ActiveStartTicks,
                ActiveEndTicks = partial ? planned.ActiveEndTicks - (endsAt - competition.EndAt).Ticks : planned.ActiveEndTicks,
                GapSeconds = planned.GapSeconds, Missed = planned.Missed, Partial = partial
            };
            db.AwdRounds.Add(round);
            rounds.Add(round);
        }
        var current = plan.Current ?? rounds.LastOrDefault(round => !round.Missed
            && round.StartsAt <= now && round.EndsAt > now);
        if (current is not null && !plan.InGap)
        {
            // A competition has one round identity, even when it has no enabled questions or teams.
            var challenges = await db.CompetitionChallenges.AsNoTracking()
                .Where(challenge => challenge.CompetitionId == competition.Id && challenge.IsPublished
                    && challenge.DeletedAt == null).Select(challenge => challenge.Id).ToArrayAsync(cancellationToken);
            foreach (var challengeId in challenges)
            {
                // This redispatch also covers a commit/publish crash. Generation is idempotent.
                await outbox.PublishAsync(new GenerateAwdFlags(competition.Id, challengeId,
                    AwdRoundSpecificationId.FromRound(current.Number), current.StartsAt, current.EndsAt));
            }
        }
        if (plan.NewRounds.Count > 0 && rounds.Any(round => round.Number < rounds[^1].Number))
            await outbox.PublishAsync(new ProjectLeaderboard(competition.Id));
        await db.SaveChangesAsync(cancellationToken);
        await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
        return plan.NewRounds.Count > 0 ? MessageExecutionOutcome.Applied
            : plan.NextRunningPosition is null ? MessageExecutionOutcome.Idempotent
            : MessageExecutionOutcome.DeferredSchedule;
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

}
