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
using NoCTF.GameModes.Awd.Flags;
using NoCTF.Infrastructure.Authentication;
using System.Security.Cryptography;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Infrastructure.Competitions.Lifecycle;

namespace NoCTF.Infrastructure.Competitions.Awd;

public sealed class AwdRoundCoordinator(
    NoCtfDbContext db,
    IAwdRoundConfigurationCatalog configurations,
    IPostCommitMessagePublisher outbox,
    TimeProvider timeProvider,
    PlatformSecretProtector secrets) : IAwdRoundCoordinator
{
    public async Task<MessageExecutionOutcome> GenerateFlagsAsync(
        GenerateAwdFlags message,
        CancellationToken cancellationToken)
    {
        if (message.ValidStart >= message.ValidUntil)
            return MessageExecutionOutcome.RejectedBusiness;
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
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

        var teams = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == message.CompetitionId
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && !team.IsBanned
                && team.DeletedAt == null)
            .OrderBy(team => team.Id)
            .Select(team => new { team.Id, team.TrackKey })
            .ToListAsync(cancellationToken);
        var teamIds = teams.Select(team => team.Id).ToArray();
        var tracks = CompetitionTrackConfiguration.EffectiveFor(target.Competition.Mode,
            target.Competition.TracksEnabled, target.Competition.Tracks);
        var competitiveTeams = teams.Where(team => (target.Competition.TracksEnabled
                ? tracks.Find(team.TrackKey) : tracks.DefaultTrack) is { EarnsScore: true, AffectsCompetitiveResults: true })
            .Select(team => team.Id).ToHashSet();
        var rules = await ResolveRoundRulesAsync(target.Competition, target.Challenge, target.Template,
            round, teamIds, competitiveTeams, handledAt, cancellationToken);
        // Pauses within this round do not count towards an active-time issuance slot.
        var clock = await AwdRunningClock.ReadAsync(db, target.Competition.Id, target.Competition.StartAt,
            handledAt, cancellationToken);
        var activeTicks = round.ActiveStartTicks + (clock.ElapsedAt(handledAt) - clock.ElapsedAt(round.StartsAt)).Ticks;
        var slot = rules.Options.GenerationPolicy switch
        {
            AwdGenerationPolicy.EveryRound => round.Number - 1L,
            AwdGenerationPolicy.EveryNRounds => (round.Number - 1L) / rules.Options.GenerationInterval,
            AwdGenerationPolicy.ActiveInterval => activeTicks / TimeSpan.FromSeconds(rules.Options.GenerationInterval).Ticks,
            _ => throw new InvalidOperationException("Unknown AWD generation policy.")
        };
        if (rules.Options.GenerationPolicy == AwdGenerationPolicy.EveryNRounds
            && (round.Number - 1) % rules.Options.GenerationInterval != 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
            return MessageExecutionOutcome.Idempotent;
        }
        var existing = await db.AwdFlagIssues.Where(issue => issue.CompetitionChallengeId == message.CompetitionChallengeId
            && issue.GenerationSlot == slot
            && db.AwdRoundRules.Any(snapshot => snapshot.Id == issue.RulesId
                && snapshot.Options.GenerationPolicy == rules.Options.GenerationPolicy)).ToListAsync(cancellationToken);
        var identities = existing.Select(issue => (issue.TeamId, issue.StoreId, issue.Ordinal)).ToHashSet();
        var runtimes = await db.RuntimeInstances.AsNoTracking()
            .Where(instance => instance.CompetitionChallengeId == message.CompetitionChallengeId
                && instance.TeamId != null && teamIds.Contains(instance.TeamId.Value)
                && instance.State == NoCTF.Domain.Runtime.RuntimeState.Running
                && instance.RunnerId != null && instance.ProviderReceipt != null)
            .Select(instance => new { TeamId = instance.TeamId!.Value, instance.Id, RunnerId = instance.RunnerId! })
            .ToListAsync(cancellationToken);
        var runtimeByTeam = runtimes.ToDictionary(instance => instance.TeamId);
        var keyRecord = await db.AwdFlagKeys.Where(key => key.CompetitionId == message.CompetitionId)
            .OrderByDescending(key => key.Version).FirstOrDefaultAsync(cancellationToken);
        byte[] key;
        if (keyRecord is null)
        {
            key = RandomNumberGenerator.GetBytes(32);
            keyRecord = new AwdFlagKey
            {
                Id = Guid.CreateVersion7(), CompetitionId = message.CompetitionId, Version = 1, CreatedAt = handledAt
            };
            keyRecord.ProtectedSecret = secrets.Protect(Convert.ToBase64String(key), PlatformSecretPurpose.AwdFlagKey,
                keyRecord.CompetitionId, keyRecord.Id);
            db.AwdFlagKeys.Add(keyRecord);
        }
        else key = Convert.FromBase64String(secrets.Unprotect(keyRecord.ProtectedSecret, PlatformSecretPurpose.AwdFlagKey,
            keyRecord.CompetitionId, keyRecord.Id));
        var added = 0;
        try
        {
            var template = new PerTeamFlagTemplate(rules.FlagHeader, rules.FlagBodyTemplate, false);
            foreach (var teamId in teamIds)
            foreach (var store in rules.Stores)
            for (var ordinal = 0; ordinal < store.FlagsPerIssue; ordinal++)
            {
                if (identities.Contains((teamId, store.StoreId, ordinal))) continue;
                var value = AwdFlagGenerator.Generate(template, key, new(message.CompetitionId,
                    message.CompetitionChallengeId, teamId, round.Id, store.StoreId, slot, ordinal, keyRecord.Version));
                var flagId = Guid.CreateVersion7();
                var issue = new AwdFlagIssue
                {
                    FlagId = flagId, CompetitionId = message.CompetitionId,
                    CompetitionChallengeId = message.CompetitionChallengeId, TeamId = teamId,
                    RoundId = round.Id, RoundNumber = round.Number, RulesId = rules.Id, StoreId = store.StoreId,
                    GenerationSlot = slot, Ordinal = ordinal, KeyVersion = keyRecord.Version,
                    Digest = Convert.ToHexString(ManageChallengeFlags.Hash(value)), State = AwdFlagIssueState.Issued,
                    DeliveryState = AwdFlagDeliveryState.Pending, Obligation = AwdFlagObligation.PlatformPending,
                    IssuedAt = handledAt, UpdatedAt = handledAt
                };
                AwdFlagLifetime.Assign(issue, rules.Options, round.Number, activeTicks);
                db.ChallengeFlags.Add(new AwdRoundChallengeFlag
                {
                    Id = flagId, CompetitionChallengeId = message.CompetitionChallengeId, TeamId = teamId,
                    Flag = value, FlagSha256 = Convert.FromHexString(issue.Digest), SpecificationKind = SpecificationKind.AwdRound,
                    SpecificationId = message.Round.Value, ValidStart = handledAt, ValidUntil = round.EndsAt,
                    CreatedAt = handledAt, AwdIssue = issue
                });
                existing.Add(issue);
                added++;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        foreach (var issue in existing.Where(issue => issue.RoundId == round.Id
                     && issue.State == AwdFlagIssueState.Issued && issue.DeliveryState == AwdFlagDeliveryState.Pending))
        {
            var store = rules.Stores.SingleOrDefault(store => store.StoreId == issue.StoreId);
            if (store?.DeliveryMode != AwdFlagDeliveryMode.RunnerCommand
                || !runtimeByTeam.TryGetValue(issue.TeamId, out var runtime)
                || (issue.DeliveryRuntimeId is not null && handledAt - issue.UpdatedAt < TimeSpan.FromSeconds(5))) continue;
            // Redispatch the same durable issuance after commit/publish failures; never derive a replacement.
            issue.DeliveryRuntimeId = runtime.Id;
            issue.UpdatedAt = handledAt;
            await outbox.PublishToRunnerNodeAsync(new InjectAwdFlag(runtime.Id, message.CompetitionChallengeId,
                issue.FlagId, round.EndsAt, runtime.RunnerId));
        }
        await db.SaveChangesAsync(cancellationToken);
        await CommitAndFlushIfOwnedAsync(transaction, cancellationToken);
        return added == 0 ? MessageExecutionOutcome.Idempotent : MessageExecutionOutcome.Applied;
    }

    private async Task<AwdRoundRules> ResolveRoundRulesAsync(Competition competition, CompetitionChallenge challenge,
        Challenge template, AwdRound round, IReadOnlyList<Guid> teams, IReadOnlySet<Guid> competitiveTeams,
        DateTimeOffset now, CancellationToken ct)
    {
        var previous = await db.AwdRoundRules.AsSplitQuery()
            .Where(rules => rules.RoundId == round.Id && rules.CompetitionChallengeId == challenge.Id)
            .OrderByDescending(rules => rules.Revision).FirstOrDefaultAsync(ct);
        if (previous is not null && teams.All(team => previous.Teams.Any(entry => entry.TeamId == team))) return previous;
        var effective = previous is null
            ? AwdEffectiveRules.Resolve((AwdCompetitionModeConfiguration)competition.ModeConfiguration!,
                (AwdCompetitionChallengeRules)challenge.Rules!) : AwdEffectiveRules.FromSnapshot(previous);
        if (effective.Validate().Count > 0) throw new InvalidOperationException("AWD effective rules are invalid.");
        var flagTemplate = previous is null
            ? AwdFlagTemplateResolver.Resolve((AwdCompetitionModeConfiguration)competition.ModeConfiguration!,
                (AwdCompetitionChallengeRules)challenge.Rules!)
            : new PerTeamFlagTemplate(previous.FlagHeader, previous.FlagBodyTemplate, false);
        if (!AwdFlagGenerator.IsValidTemplate(flagTemplate)) throw new InvalidOperationException("AWD flag template is insecure.");
        var stores = previous is null
            ? ((AwdChallengeDefinition)template.Definition!).FlagStores.Select(store =>
            {
                var overrideRule = ((AwdCompetitionChallengeRules)challenge.Rules!).StoreRules.SingleOrDefault(rule => rule.StoreId == store.Id);
                return new AwdRoundStoreBudget
                {
                    StoreId = store.Id, Name = store.Name, Weight = overrideRule?.Weight ?? 1m,
                    FlagsPerIssue = overrideRule?.FlagsPerIssue ?? effective.Options.FlagsPerStore,
                    DeliveryMode = store.DeliveryMode, InjectionCommand = store.InjectionCommand,
                    TargetServiceName = store.TargetServiceName, InjectionTimeoutSeconds = store.InjectionTimeoutSeconds,
                    PreservesHistoricalFlags = store.PreservesHistoricalFlags
                };
            }).ToList()
            : previous.Stores.Select(store => new AwdRoundStoreBudget
            {
                StoreId = store.StoreId, Name = store.Name, Weight = store.Weight, FlagsPerIssue = store.FlagsPerIssue,
                DeliveryMode = store.DeliveryMode, InjectionCommand = store.InjectionCommand,
                TargetServiceName = store.TargetServiceName, InjectionTimeoutSeconds = store.InjectionTimeoutSeconds,
                PreservesHistoricalFlags = store.PreservesHistoricalFlags
            }).ToList();
        if (stores.Count is < 1 or > 64 || stores.Any(store => store.Weight <= 0 || decimal.Round(store.Weight, 6) != store.Weight
                || store.FlagsPerIssue is < 1 or > 64))
            throw new InvalidOperationException("AWD requires valid store budgets.");
        // Existing members retain their round qualification when a newly admitted team adds a revision.
        var eligibility = teams.Select(team => new AwdRoundTeamEligibility { TeamId = team,
            Competitive = previous?.Teams.SingleOrDefault(entry => entry.TeamId == team)?.Competitive
                ?? competitiveTeams.Contains(team) }).ToList();
        var qualifiedTeams = eligibility.Where(team => team.Competitive).Select(team => team.TeamId).ToArray();
        var ranks = effective.AttackMode == AwdAttackRewardMode.TargetRank
            ? await AwdSettledRankingReader.ReadAsync(db, competition, round.Number, qualifiedTeams, now, ct)
            : new Dictionary<Guid, int>();
        foreach (var team in eligibility)
            team.SettledRank = ranks.TryGetValue(team.TeamId, out var rank) ? rank : null;
        var snapshot = new AwdRoundRules
        {
            Id = Guid.CreateVersion7(), CompetitionId = competition.Id, CompetitionChallengeId = challenge.Id,
            RoundId = round.Id, Revision = (previous?.Revision ?? 0) + 1, EffectiveAt = now,
            AttackRewardMode = effective.AttackMode, AttackPoints = effective.AttackPoints,
            VictimDefensePoolPoints = effective.DefensePoints, ServiceHealthyPoints = effective.SlaPoints,
            CheckerIntervalSeconds = effective.CheckerIntervalSeconds, Options = effective.Options with { },
            EligibleTeamCount = qualifiedTeams.Length, Teams = eligibility,
            FlagHeader = flagTemplate.Header, FlagBodyTemplate = flagTemplate.BodyTemplate, Stores = stores
        };
        db.AwdRoundRules.Add(snapshot);
        return snapshot;
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
