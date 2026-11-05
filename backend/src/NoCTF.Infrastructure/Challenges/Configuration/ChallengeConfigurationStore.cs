using NoCTF.Application.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.Infrastructure.Challenges.Configuration;

public sealed class ChallengeConfigurationStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    ICompetitionEventRecorder events) : IChallengeConfigurationStore
{
    public async Task<ChallengeConfigurationView?> FindAsync(
        Guid competitionId,
        Guid challengeId,
        CancellationToken ct)
    {
        var item = await db.CompetitionChallenges.AsNoTracking()
            .Join(db.Challenges.AsNoTracking(), configuration => configuration.ChallengeId, challenge => challenge.Id,
                (configuration, challenge) => new { Configuration = configuration, Challenge = challenge })
            .Join(db.Competitions.AsNoTracking(), item => item.Configuration.CompetitionId, competition => competition.Id,
                (item, competition) => new { item.Configuration, item.Challenge, Competition = competition })
            .Where(item => item.Configuration.Id == challengeId
                           && item.Configuration.CompetitionId == competitionId
                           && item.Challenge.DeletedAt == null
                           && item.Competition.DeletedAt == null)
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);
        if (item?.Configuration.Rules is null
            || item.Challenge.Definition is null
            || item.Competition.ModeConfiguration is null)
            return null;
        var eligibleTeamCount = await db.Teams.CountAsync(team =>
            team.CompetitionId == item.Competition.Id
            && team.RegistrationStatus == TeamRegistrationStatus.Approved
            && !team.IsBanned
            && team.DeletedAt == null, ct);
        var presets = await db.Set<NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset>().AsNoTracking()
            .Where(preset => preset.CompetitionId == competitionId).ToArrayAsync(ct);
        var matched = NoCTF.Domain.Competitions.Scoring.CompetitionScorePreset.Match(
            item.Configuration.Tags.Select(tag => tag.Name), presets);
        var presetChallengeCount = matched is null ? 0 : await db.CompetitionChallenges.IgnoreQueryFilters()
            .CountAsync(challenge => challenge.CompetitionId == competitionId
                && challenge.Tags.Any(tag => tag.NormalizedName == matched.NormalizedName), ct);
        return new(
            item.Competition.Id,
            item.Configuration.Id,
            item.Competition.Mode,
            item.Configuration.Rules,
            item.Competition.ModeConfiguration,
            item.Competition.Status,
            eligibleTeamCount,
            item.Configuration.UpdatedAt,
            item.Challenge.Definition)
        {
            ScorePreset = matched,
            ScorePresetChallengeCount = presetChallengeCount
        };
    }

    public async Task<ChallengeConfigurationUpdateResult> TryUpdateAsync(
        Guid competitionId,
        Guid challengeId,
        CompetitionChallengeRules rules,
        DateTimeOffset updatedAt,
        CancellationToken ct, AwdConfigurationChangeAudit? audit = null)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var status = await CompetitionStateReader.ReadAsync(db, competitionId, ct);
        if (status is null) return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var competition = await db.Competitions.AsNoTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, ct);
        if (competition is null)
            return new(null, ChallengeConfigurationUpdateFailure.CompetitionNotFound);
        var tracked = await db.CompetitionChallenges
            .AsSplitQuery()
            .SingleOrDefaultAsync(challenge => challenge.Id == challengeId
                && challenge.CompetitionId == competitionId
                && challenge.DeletedAt == null, ct);
        if (tracked?.Rules is null)
            return new(null, ChallengeConfigurationUpdateFailure.ChallengeNotFound);
        if (tracked.Mode != rules.Mode || tracked.Rules.GetType() != rules.GetType())
            throw new InvalidOperationException("Challenge rules type does not match its mode.");
        if (tracked.Mode == GameMode.LiveSolo && await db.LiveSoloRoundQuestions.Where(question => question.CompetitionChallengeId == challengeId)
                .Join(db.LiveSoloRounds, question => question.RoundId, round => round.Id, (_, round) => round)
                .AnyAsync(round => round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.Preparing
                    || round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.Countdown || round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.Running
                    || round.State == NoCTF.Domain.LiveSolo.LiveSoloRoundState.ConfirmingResult, ct))
            return new(null, ChallengeConfigurationUpdateFailure.ConfigurationLocked);
        var nextAwdRound = tracked.Mode == GameMode.Awd
            ? (await db.AwdRounds.Where(round => round.CompetitionId == competitionId).MaxAsync(round => (int?)round.Number, ct) ?? 0) + 1
            : (int?)null;
        if (nextAwdRound > 1 && audit?.HasValidReason != true)
            return new(null, ChallengeConfigurationUpdateFailure.RuleChangeReasonRequired);
        rules.CompetitionChallengeId = challengeId;
        var bloodChanged = tracked.Rules is CtfCompetitionChallengeRules beforeCtf && rules is CtfCompetitionChallengeRules afterCtf
            && !beforeCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value))
                .SequenceEqual(afterCtf.BloodRewards.OrderBy(item => item.Position).Select(item => (item.Policy, item.Value)));
        var storesChanged = tracked.Rules is AwdCompetitionChallengeRules beforeAwd
            && rules is AwdCompetitionChallengeRules afterAwd
            && !beforeAwd.StoreRules.OrderBy(item => item.StoreId).Select(item => (item.StoreId, item.Weight, item.FlagsPerIssue))
                .SequenceEqual(afterAwd.StoreRules.OrderBy(item => item.StoreId).Select(item => (item.StoreId, item.Weight, item.FlagsPerIssue)));
        if (rules is AwdCompetitionChallengeRules requestedAwd)
        {
            var definition = await db.Set<ChallengeDefinition>().AsNoTracking().AsSplitQuery()
                .SingleAsync(item => item.ChallengeId == tracked.ChallengeId, ct);
            var storeIds = definition.Break!.FlagStores.Select(item => item.Id).ToArray();
            if (requestedAwd.StoreRules.Any(item => !storeIds.Contains(item.StoreId)))
                return new(null, ChallengeConfigurationUpdateFailure.ConfigurationLocked);
        }
        db.Entry(tracked.Rules).CurrentValues.SetValues(rules);
        if (tracked.Rules is IChallengeRuntimeRules storedRuntime && rules is IChallengeRuntimeRules requestedRuntime)
            storedRuntime.Runtime = new() { Allocation = requestedRuntime.Runtime.Allocation, FlagSource = requestedRuntime.Runtime.FlagSource,
                TtlSeconds = requestedRuntime.Runtime.TtlSeconds };
        if (tracked.Rules is IChallengeFlagTemplateOverride storedFlag && rules is IChallengeFlagTemplateOverride requestedFlag)
            storedFlag.FlagTemplate = new() { Header = requestedFlag.FlagTemplate.Header, BodyTemplate = requestedFlag.FlagTemplate.BodyTemplate,
                LeetLiteralText = requestedFlag.FlagTemplate.LeetLiteralText };
        if (tracked.Rules is CtfCompetitionChallengeRules storedCurve && rules is CtfCompetitionChallengeRules requestedCurve)
            storedCurve.ScoreCurve = requestedCurve.ScoreCurve;
        if (tracked.Rules is AwdpCompetitionChallengeRules storedAwdp && rules is AwdpCompetitionChallengeRules requestedAwdp)
        {
            storedAwdp.BreakScoreCurve = requestedAwdp.BreakScoreCurve;
            storedAwdp.FixScoreCurve = requestedAwdp.FixScoreCurve;
        }
        if (tracked.Rules is AwdCompetitionChallengeRules currentAwd && rules is AwdCompetitionChallengeRules nextAwd)
            currentAwd.AwdOverrides = nextAwd.AwdOverrides with { };
        db.ChangeTracker.DetectChanges();
        if (db.Entry(tracked.Rules).State == EntityState.Unchanged && !bloodChanged && !storesChanged)
        {
            var unchanged = await FindAsync(competitionId, challengeId, ct);
            await transaction.CommitAsync(ct);
            return new(unchanged);
        }
        if (bloodChanged && tracked.Rules is CtfCompetitionChallengeRules storedCtf && rules is CtfCompetitionChallengeRules nextCtf)
        {
            db.Set<CompetitionChallengeBloodReward>().RemoveRange(storedCtf.BloodRewards);
            storedCtf.BloodRewards = nextCtf.BloodRewards.Select((reward, position) =>
                new CompetitionChallengeBloodReward
                {
                    CompetitionChallengeId = challengeId,
                    Position = position,
                    Policy = reward.Policy,
                    Value = reward.Value
                }).ToList();
        }
        if (storesChanged && tracked.Rules is AwdCompetitionChallengeRules storedAwd
            && rules is AwdCompetitionChallengeRules requestedStores)
        {
            var requestedIds = requestedStores.StoreRules.Select(item => item.StoreId).ToHashSet();
            foreach (var old in storedAwd.StoreRules.Where(item => !requestedIds.Contains(item.StoreId)).ToArray())
            {
                storedAwd.StoreRules.Remove(old);
                db.Remove(old);
            }
            foreach (var item in requestedStores.StoreRules)
            {
                var existing = storedAwd.StoreRules.SingleOrDefault(old => old.StoreId == item.StoreId);
                if (existing is null)
                    storedAwd.StoreRules.Add(new() { CompetitionChallengeId = challengeId,
                        StoreId = item.StoreId, Weight = item.Weight, FlagsPerIssue = item.FlagsPerIssue });
                else
                {
                    existing.Weight = item.Weight;
                    existing.FlagsPerIssue = item.FlagsPerIssue;
                }
            }
        }
        tracked.UpdatedAt = updatedAt;
        if (competition.Mode == GameMode.Awd
            && status == CompetitionStatus.Running
            && tracked.IsPublished)
        {
            await outbox.PublishAsync(new AdvanceAwdRound(
                competitionId,
                updatedAt));
        }
        await events.RecordAsync(new(
            competitionId,
            CompetitionEventKind.ChallengeUpdated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            updatedAt,
            CompetitionChallengeId: challengeId,
            CompetitionStatus: status, ActorUserId: audit?.ActorUserId, Reason: audit?.Reason?.Trim(),
            AwdRuleTiming: nextAwdRound is null ? null : NoCTF.Domain.Competitions.Awd.AwdRuleChangeTiming.NextRound,
            AwdEffectiveFromRound: nextAwdRound), ct);
        await db.SaveChangesAsync(ct);
        var result = await FindAsync(competitionId, challengeId, ct);
        await transaction.CommitAsync(ct);
        await transaction.FlushMessagesAsync(outbox);
        return new(result);
    }
}
