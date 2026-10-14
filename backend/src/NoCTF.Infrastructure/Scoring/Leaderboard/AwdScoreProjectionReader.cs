using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

internal static class AwdScoreProjectionReader
{
    public static async Task<IReadOnlyList<AwdRoundScoreProjection>> ReadAsync(NoCtfDbContext db,
        Guid competitionId, IReadOnlyList<LeaderboardChallengeFact> challenges,
        IReadOnlyList<LeaderboardTeamFact> teams, DateTimeOffset projectedAt, CancellationToken ct,
        NoCTF.Application.Competitions.Awd.AwdScoringProjectionOverride? candidate = null)
    {
        var challengeIds = challenges.Where(x => !x.IsDeleted).Select(x => x.Id).ToArray();
        // The full projection needs the competition clock, not its collaborators, webhooks or content.
        var competition = await db.Competitions.AsNoTracking().IgnoreAutoIncludes().Where(x => x.Id == competitionId)
            .Select(x => new
            {
                x.StartAt, x.EndAt,
                Configuration = new AwdCompetitionModeConfiguration
                {
                    HardeningDurationSeconds = ((AwdCompetitionModeConfiguration)x.ModeConfiguration!).HardeningDurationSeconds,
                    RoundDurationSeconds = ((AwdCompetitionModeConfiguration)x.ModeConfiguration!).RoundDurationSeconds,
                    AwdOptions = new AwdRuleOptions
                    {
                        TotalRounds = ((AwdCompetitionModeConfiguration)x.ModeConfiguration!).AwdOptions.TotalRounds,
                        RoundGapSeconds = ((AwdCompetitionModeConfiguration)x.ModeConfiguration!).AwdOptions.RoundGapSeconds,
                        SubmissionTailSeconds = ((AwdCompetitionModeConfiguration)x.ModeConfiguration!).AwdOptions.SubmissionTailSeconds
                    }
                }
            }).SingleAsync(ct);
        var rounds = await db.AwdRounds.AsNoTracking().IgnoreAutoIncludes().Where(x => x.CompetitionId == competitionId
            && x.StartsAt <= projectedAt).OrderBy(x => x.Number)
            .Select(x => new AwdRound
            {
                Id = x.Id, Number = x.Number, StartsAt = x.StartsAt, EndsAt = x.EndsAt,
                ActiveStartTicks = x.ActiveStartTicks, ActiveEndTicks = x.ActiveEndTicks,
                Missed = x.Missed, Partial = x.Partial
            }).ToArrayAsync(ct);
        if (rounds.Length == 0) return [];
        var roundIds = rounds.Select(x => x.Id).ToArray();
        var rules = await db.AwdRoundRules.AsNoTracking().IgnoreAutoIncludes().AsSplitQuery()
            .Where(x => x.CompetitionId == competitionId && challengeIds.Contains(x.CompetitionChallengeId)
                && roundIds.Contains(x.RoundId) && x.EffectiveAt <= projectedAt)
            .Select(x => new AwdRoundRules
            {
                Id = x.Id, CompetitionId = x.CompetitionId, RoundId = x.RoundId,
                CompetitionChallengeId = x.CompetitionChallengeId, Revision = x.Revision, EffectiveAt = x.EffectiveAt,
                AttackRewardMode = x.AttackRewardMode, AttackPoints = x.AttackPoints,
                VictimDefensePoolPoints = x.VictimDefensePoolPoints, ServiceHealthyPoints = x.ServiceHealthyPoints,
                CheckerIntervalSeconds = x.CheckerIntervalSeconds, EligibleTeamCount = x.EligibleTeamCount, Options = x.Options,
                Stores = x.Stores.Select(store => new AwdRoundStoreBudget
                {
                    StoreId = store.StoreId, Weight = store.Weight, FlagsPerIssue = store.FlagsPerIssue
                }).ToList(),
                Teams = x.Teams.Select(team => new AwdRoundTeamEligibility
                {
                    TeamId = team.TeamId, Competitive = team.Competitive, SettledRank = team.SettledRank
                }).ToList()
            }).ToArrayAsync(ct);
        var roundNumbers = rounds.ToDictionary(round => round.Id, round => round.Number);
        var changes = await db.Set<AwdScoringRuleChange>().AsNoTracking()
            .Where(change => change.CompetitionId == competitionId && change.AppliedAt <= projectedAt)
            .OrderBy(change => change.Sequence).ToArrayAsync(ct);
        var changesByChallenge = changes.ToLookup(change => change.CompetitionChallengeId);
        rules = rules.Select(rule =>
        {
            var number = roundNumbers[rule.RoundId];
            var change = changesByChallenge[rule.CompetitionChallengeId]
                .LastOrDefault(item => number >= item.FirstRound && number <= item.LastRound);
            var policy = candidate is not null && candidate.CompetitionChallengeId == rule.CompetitionChallengeId
                && number >= candidate.FirstRound && number <= candidate.LastRound ? candidate.Policy : change?.Policy;
            return policy?.Apply(rule) ?? rule;
        }).ToArray();
        var issues = await db.AwdFlagIssues.AsNoTracking().Where(x => x.CompetitionId == competitionId
            && challengeIds.Contains(x.CompetitionChallengeId) && x.IssuedAt <= projectedAt
            && db.ChallengeFlags.Any(flag => flag.Id == x.FlagId && flag.DeletedAt == null))
            .Select(x => new AwdFlagIssue
            {
                FlagId = x.FlagId, CompetitionChallengeId = x.CompetitionChallengeId, TeamId = x.TeamId,
                RoundId = x.RoundId, RoundNumber = x.RoundNumber, RulesId = x.RulesId, StoreId = x.StoreId,
                State = x.State, ActiveStartTicks = x.ActiveStartTicks, ActiveExpiryTicks = x.ActiveExpiryTicks,
                ExpiryRound = x.ExpiryRound, HistoricalActiveExpiryTicks = x.HistoricalActiveExpiryTicks,
                HistoricalExpiryRound = x.HistoricalExpiryRound
            }).ToArrayAsync(ct);
        var issueById = issues.ToDictionary(x => x.FlagId);
        var timing = challenges.ToDictionary(x => x.Id, x => x.Timing);
        var captures = await db.AwdCaptureIdentities.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Join(db.GameplayFacts.AsNoTracking().Where(x => x.OccurredAt <= projectedAt
                    && x.State == GameplayFactState.Completed && x.Result == GameplayFactResult.Correct
                    && x.Awd != null && x.Awd.ReceiptScorable == true),
                identity => identity.GameplayFactId, fact => fact.Id,
                (identity, fact) => new AwdScoringCapture(identity.FlagId, identity.AttackingTeamId, fact.OccurredAt))
            .ToArrayAsync(ct);
        // Include planned samples, including those not dispatched: absence must never become a pass.
        var checkFacts = await db.GameplayFacts.AsNoTracking().IgnoreAutoIncludes().AsSplitQuery()
            .Where(x => x.CompetitionId == competitionId && x.Kind == GameplayFactKind.AwdServiceTransition
                && x.Awd != null && x.TeamId != null && x.ReferenceId != null
                && roundIds.Contains(x.ReferenceId.Value) && x.CompetitionChallengeId != null
                && challengeIds.Contains(x.CompetitionChallengeId.Value))
            .Select(x => new
            {
                TeamId = x.TeamId!.Value, CompetitionChallengeId = x.CompetitionChallengeId!.Value,
                RoundId = x.ReferenceId!.Value, x.UpdatedAt,
                Evidence = new AwdFactDetails
                {
                    RulesId = x.Awd!.RulesId, Execution = x.Awd.Execution, Settlement = x.Awd.Settlement,
                    Health = x.Awd.Health, ObservedAt = x.Awd.ObservedAt, ReceivedAt = x.Awd.ReceivedAt,
                    AdjudicatedAt = x.Awd.AdjudicatedAt, AttemptStartedAt = x.Awd.AttemptStartedAt,
                    SampleActiveTicks = x.Awd.SampleActiveTicks, SampleDeadlineActiveTicks = x.Awd.SampleDeadlineActiveTicks,
                    FinalScheduledCheck = x.Awd.FinalScheduledCheck, ManifestComplete = x.Awd.ManifestComplete,
                    RequiredFlags = x.Awd.RequiredFlags, ReadableFlags = x.Awd.ReadableFlags,
                    Flags = x.Awd.Flags.Select(flag => new AwdFactFlagEvidence
                    {
                        FlagId = flag.FlagId, Obligation = flag.Obligation, Read = flag.Read
                    }).ToList()
                }
            })
            .ToArrayAsync(ct);
        var clock = await AwdRunningClock.ReadAsync(db, competitionId, competition.StartAt, projectedAt, ct);
        var latest = rounds[^1];
        long ActiveAt(AwdRound round, DateTimeOffset at) => Math.Clamp(round.ActiveStartTicks
            + (clock.ElapsedAt(at) - clock.ElapsedAt(round.StartsAt)).Ticks, round.ActiveStartTicks, round.ActiveEndTicks);
        var activeTicks = ActiveAt(latest, projectedAt);
        var roundById = rounds.ToDictionary(x => x.Id);
        var checks = checkFacts.Select(fact =>
        {
            var evidence = AwdEvidenceCutoff.Read(fact.Evidence, fact.UpdatedAt, projectedAt);
            return new AwdScoringCheck(fact.TeamId, fact.CompetitionChallengeId, fact.RoundId,
                evidence, evidence.ObservedAt is { } at ? ActiveAt(roundById[fact.RoundId], at) : null);
        }).ToArray();
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, competitionId, competition.StartAt,
            competition.EndAt, competition.Configuration, projectedAt, ct);
        var closed = !deadline.EndIsProvisional && projectedAt >= deadline.AcceptanceEndsAt;
        var eligibleCaptures = captures.Where(x => issueById.TryGetValue(x.FlagId, out var issue)
            && (timing[issue.CompetitionChallengeId]?.CanScore(x.ReceivedAt) ?? true)).ToArray();
        var scores = AwdRoundScoreProjector.Project(rounds, rules, issues, eligibleCaptures, checks,
            teams.Where(x => !x.IsBanned && !x.IsDeleted && x.EarnsScore).Select(x => x.Id).ToHashSet(),
            activeTicks, latest.Number, closed,
            rules.ToDictionary(x => x.Id, x => ActiveAt(roundById[x.RoundId], x.EffectiveAt)),
            !deadline.EndIsProvisional && projectedAt >= deadline.EndsAt ? ActiveAt(latest, deadline.EndsAt) : null);
        return scores.Select(score => timing[score.CompetitionChallengeId]?.CanScoreInterval(
                roundById[score.RoundId].StartsAt, roundById[score.RoundId].EndsAt) != false ? score
            : score with { Components = score.Components.Where(x => x.Component is not
                    (AwdScoreComponent.Availability or AwdScoreComponent.ExtraPenalty)).ToArray(),
                CompleteHealthyRound = false }).ToArray();
    }

    public static IReadOnlyList<LeaderboardAwdAggregateFact> Aggregate(IReadOnlyList<AwdRoundScoreProjection> rounds) =>
        rounds.GroupBy(x => (x.TeamId, x.CompetitionChallengeId)).Select(group => new LeaderboardAwdAggregateFact(
            group.Key.TeamId, group.Key.CompetitionChallengeId, group.Sum(x => x.Score), group.Sum(x => x.AttackPoints),
            group.Sum(x => x.AttackCount), group.Count(x => x.CompleteHealthyRound), group.Max(x => x.LastAttackAt),
            group.Sum(x => x.PositivePoints))).ToArray();
}
