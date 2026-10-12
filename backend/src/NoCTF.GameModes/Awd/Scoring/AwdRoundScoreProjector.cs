using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.GameModes.Awd.Checking;
using NoCTF.GameModes.Awd.Configuration;

namespace NoCTF.GameModes.Awd.Scoring;

public sealed record AwdScoringCapture(Guid FlagId, Guid TeamId, DateTimeOffset ReceivedAt);
public sealed record AwdScoringCheck(Guid TeamId, Guid CompetitionChallengeId, Guid RoundId,
    AwdFactDetails Evidence, long? ObservedActiveTicks);

/// <summary>One calculation shared by total scores, round cells and explanations.</summary>
public static class AwdRoundScoreProjector
{
    public static IReadOnlyList<AwdRoundScoreProjection> Project(
        IReadOnlyList<AwdRound> rounds, IReadOnlyList<AwdRoundRules> rules,
        IReadOnlyList<AwdFlagIssue> issues, IReadOnlyList<AwdScoringCapture> captures,
        IReadOnlyList<AwdScoringCheck> checks, IReadOnlySet<Guid> scoringTeams,
        long activeTicks, int currentRound, bool acceptanceClosed,
        IReadOnlyDictionary<Guid, long>? ruleEnabledActiveTicks = null,
        long? competitionStoppedActiveTicks = null)
    {
        var roundById = rounds.ToDictionary(x => x.Id);
        var ruleById = rules.ToDictionary(x => x.Id);
        var issueById = issues.ToDictionary(x => x.FlagId);
        var capturesByFlag = captures.GroupBy(x => x.FlagId).ToDictionary(x => x.Key,
            x => x.GroupBy(capture => capture.TeamId).Select(team => team.MinBy(capture => capture.ReceivedAt)!).ToArray());
        var checksByScope = checks.GroupBy(x => (x.TeamId, x.CompetitionChallengeId, x.RoundId))
            .ToDictionary(x => x.Key, x => x.OrderBy(check => check.Evidence.SampleActiveTicks).ToArray());
        var values = new Dictionary<(Guid TeamId, Guid ChallengeId, Guid RoundId), RoundAccumulator>();
        var availability = new Dictionary<(Guid TeamId, Guid ChallengeId, Guid RoundId), Availability>();
        var streaks = new Dictionary<(Guid TeamId, Guid ChallengeId), (int Round, int Count)>();

        RoundAccumulator Get(Guid team, Guid challenge, Guid round)
        {
            var key = (team, challenge, round);
            if (!values.TryGetValue(key, out var value)) values[key] = value = new();
            return value;
        }

        // SLA belongs to a question/round, independent of the number of issued flags or containers.
        foreach (var scope in rules.SelectMany(rule => rule.Teams.Where(team => team.Competitive)
                     .Select(team => (Rule: rule, Team: team.TeamId)))
                     .Where(x => scoringTeams.Contains(x.Team) && roundById.ContainsKey(x.Rule.RoundId))
                     .GroupBy(x => (x.Team, x.Rule.CompetitionChallengeId, x.Rule.RoundId))
                     .OrderBy(x => roundById[x.Key.RoundId].Number))
        {
            var key = scope.Key;
            var samples = checksByScope.GetValueOrDefault(key, []);
            // A revision admitting a new team does not replace checks already planned for existing teams.
            var plannedRulesId = samples.Where(x => x.Evidence.FinalScheduledCheck)
                .OrderBy(x => x.Evidence.SampleActiveTicks).ThenBy(x => ruleById[x.Evidence.RulesId!.Value].Revision)
                .LastOrDefault()?.Evidence.RulesId;
            var snapshot = plannedRulesId is { } id ? ruleById[id] : scope.OrderBy(x => x.Rule.Revision).First().Rule;
            var round = roundById[snapshot.RoundId];
            var result = ResolveAvailability(round, snapshot, samples, issueById, ruleById, activeTicks,
                ruleEnabledActiveTicks?.GetValueOrDefault(snapshot.Id) ?? round.ActiveStartTicks);
            availability[key] = result;
            var accumulator = Get(key.Team, key.CompetitionChallengeId, key.RoundId);
            accumulator.Health = result.Health;
            accumulator.CompleteHealthyRound = result.State == AwdSettlementState.Final && result.CompletelyHealthy;
            var effective = AwdEffectiveRules.FromSnapshot(snapshot);
            var streakKey = (key.Team, key.CompetitionChallengeId);
            var prior = streaks.GetValueOrDefault(streakKey);
            var failed = result.State == AwdSettlementState.Final
                && result.Health is AwdHealth.Mumble or AwdHealth.Offline or AwdHealth.Recovering;
            var streak = failed ? prior.Round == round.Number - 1 ? prior.Count + 1 : 1 : 0;
            streaks[streakKey] = (round.Number, streak);
            var sla = result.Ratio is { } ratio ? ratio * effective.SlaPoints : (AwdScoreFraction?)null;
            if (effective.Options.Composition == AwdScoreComposition.PositiveSlaMultiplier) sla = 0;
            else if (effective.Options.Composition == AwdScoreComposition.Weighted) sla *= effective.Options.SlaWeight;
            accumulator.Add(AwdScoreComponent.Availability, sla, result.State);
            var penalty = result.Penalty;
            if (penalty is { } knownPenalty && streak > 1)
            {
                var cap = effective.Options.MaximumRoundPenalty ?? NoCTF.Application.Scoring.ScoreValueLimits.MaximumConfiguredValue;
                for (var index = 1; index < streak && knownPenalty < cap; index++)
                {
                    knownPenalty *= effective.Options.ConsecutiveFailureMultiplier;
                    if (knownPenalty > cap) knownPenalty = cap;
                }
                penalty = knownPenalty;
            }
            accumulator.Add(AwdScoreComponent.ExtraPenalty, penalty is { } amount ? 0 - amount : null, result.State);
        }

        foreach (var issue in issues.Where(x => x.State == AwdFlagIssueState.Issued && ruleById.ContainsKey(x.RulesId)))
        {
            var snapshot = ruleById[issue.RulesId];
            if (!roundById.TryGetValue(issue.RoundId, out var round)) continue;
            var store = snapshot.Stores.Single(x => x.StoreId == issue.StoreId);
            var share = AwdScoreCalculator.BudgetShare(store.Weight, snapshot.Stores.Sum(x => x.Weight), store.FlagsPerIssue);
            var effective = AwdEffectiveRules.FromSnapshot(snapshot);
            var eligible = snapshot.Teams.Where(x => x.Competitive).Select(x => x.TeamId).ToHashSet();
            if (!eligible.Contains(issue.TeamId)) continue;
            var attacks = capturesByFlag.GetValueOrDefault(issue.FlagId, [])
                .Where(x => x.TeamId != issue.TeamId && eligible.Contains(x.TeamId) && eligible.Contains(issue.TeamId)).ToArray();
            var closed = acceptanceClosed || issue.ExpiryRound is { } expiresRound && currentRound >= expiresRound
                || issue.ActiveExpiryTicks is { } expiresTicks && activeTicks >= expiresTicks;
            var finalAttack = effective.Options.AttackSettlement switch
            {
                AwdAttackSettlement.Immediate => true,
                AwdAttackSettlement.RoundEnd => activeTicks >= round.ActiveEndTicks,
                AwdAttackSettlement.FlagExpiry => closed,
                _ => throw new ArgumentOutOfRangeException(nameof(effective.Options.AttackSettlement))
            };
            var state = finalAttack ? AwdSettlementState.Final : AwdSettlementState.Provisional;
            foreach (var capture in attacks.Where(x => scoringTeams.Contains(x.TeamId)))
            {
                var attacker = Get(capture.TeamId, issue.CompetitionChallengeId, issue.RoundId);
                var reward = AwdScoreCalculator.Attack(effective, share, attacks.Length, snapshot.EligibleTeamCount,
                    snapshot.Teams.Single(x => x.TeamId == issue.TeamId).SettledRank);
                var scaled = ScalePositive(reward, effective.Options,
                    availability.GetValueOrDefault((capture.TeamId, issue.CompetitionChallengeId, issue.RoundId)), state, true);
                attacker.Add(AwdScoreComponent.Attack, scaled.Amount, scaled.State);
                attacker.AttackCount++;
                if (attacker.LastAttackAt is null || capture.ReceivedAt > attacker.LastAttackAt) attacker.LastAttackAt = capture.ReceivedAt;
            }
            if (!scoringTeams.Contains(issue.TeamId)) continue;
            var defender = Get(issue.TeamId, issue.CompetitionChallengeId, issue.RoundId);
            var loss = AwdScoreCalculator.DefenseLoss(effective, share, attacks.Length);
            if (effective.Options.Composition == AwdScoreComposition.Weighted) loss *= effective.Options.DefenseWeight;
            defender.Add(AwdScoreComponent.DefenseLoss, 0 - loss,
                !closed && (attacks.Length == 0 || effective.Options.DefenseMode == AwdDefenseMode.PerCapture)
                    ? AwdSettlementState.Provisional : AwdSettlementState.Final);
            if (effective.Options.ConfidentialityPoints == 0) continue;
            if (attacks.Length > 0)
            {
                defender.Add(AwdScoreComponent.DefenseReward, 0, AwdSettlementState.Final);
                continue;
            }
            var coverage = closed ? ReadableCoverage(issue, checks, rounds, rules,
                ruleEnabledActiveTicks, competitionStoppedActiveTicks, currentRound) : null;
            var rewardState = closed ? coverage is null ? AwdSettlementState.PendingAdjudication : AwdSettlementState.Final
                : AwdSettlementState.Provisional;
            var confidentiality = coverage is { } readable ? effective.Options.ConfidentialityPoints * share * readable : (AwdScoreFraction?)null;
            var scaledDefense = ScalePositive(confidentiality, effective.Options,
                availability.GetValueOrDefault((issue.TeamId, issue.CompetitionChallengeId, issue.RoundId)), rewardState, false);
            defender.Add(AwdScoreComponent.DefenseReward, scaledDefense.Amount, scaledDefense.State);
        }

        return values.Select(pair => new AwdRoundScoreProjection(pair.Key.TeamId, pair.Key.ChallengeId,
            pair.Key.RoundId, roundById[pair.Key.RoundId].Number, pair.Value.Components.OrderBy(x => x.Key)
                .Select(x => x.Value.Project(x.Key)).ToArray(), pair.Value.AttackCount, pair.Value.LastAttackAt,
            pair.Value.Health, pair.Value.CompleteHealthyRound, activeTicks >= roundById[pair.Key.RoundId].ActiveEndTicks)).ToArray();
    }

    private static (AwdScoreFraction? Amount, AwdSettlementState State) ScalePositive(AwdScoreFraction? amount,
        AwdRuleOptions options, Availability? availability, AwdSettlementState state, bool attack)
    {
        if (options.Composition == AwdScoreComposition.Weighted)
            amount *= attack ? options.AttackWeight : options.DefenseWeight;
        if (options.Composition != AwdScoreComposition.PositiveSlaMultiplier) return (amount, state);
        if (availability?.Ratio is not { } ratio) return (null, availability?.State ?? AwdSettlementState.PendingAdjudication);
        return (amount * ratio, Merge(state, availability.State));
    }

    private static Availability ResolveAvailability(AwdRound round, AwdRoundRules snapshot,
        IReadOnlyList<AwdScoringCheck> checks, IReadOnlyDictionary<Guid, AwdFlagIssue> issues,
        IReadOnlyDictionary<Guid, AwdRoundRules> ruleById, long activeTicks,
        long enabledActiveTicks)
    {
        var closed = activeTicks >= round.ActiveEndTicks;
        var unresolved = closed ? AwdSettlementState.PendingAdjudication : AwdSettlementState.Provisional;
        if (round.Partial || round.Missed) return new(null, null, null, unresolved);
        var effective = AwdEffectiveRules.FromSnapshot(snapshot);
        var samples = checks.Where(x => x.Evidence.RulesId == snapshot.Id).ToArray();
        if (samples.Length == 0) return new(null, null, null, unresolved);
        var expected = AwdCheckPlan.Create(round, snapshot, enabledActiveTicks);
        var selected = snapshot.Options.SlaSettlement == AwdSlaSettlement.FinalScheduledCheck
            ? samples.Where(x => x.Evidence.FinalScheduledCheck).ToArray() : samples;
        if (selected.Length == 0 || snapshot.Options.SlaSettlement != AwdSlaSettlement.FinalScheduledCheck
            && expected.Any(x => !samples.Any(sample => sample.Evidence.SampleActiveTicks == x.ActiveTicks)))
            return new(null, null, null, unresolved);
        AwdScoreFraction numerator = 0;
        AwdScoreFraction penalties = 0;
        AwdScoreFraction denominator = 0;
        var ordered = selected.OrderBy(x => x.ObservedActiveTicks ?? long.MaxValue).ToArray();
        if (snapshot.Options.SlaSettlement == AwdSlaSettlement.TimeWeighted
            && ordered[0].ObservedActiveTicks != expected[0].ActiveTicks)
            return new(null, null, null, unresolved);
        for (var index = 0; index < ordered.Length; index++)
        {
            var check = ordered[index];
            var details = check.Evidence;
            if (details.Settlement == AwdSettlementState.Exempted) continue;
            if (details.Settlement != AwdSettlementState.Final || !details.ManifestComplete
                || details.Health is null or AwdHealth.InternalError)
                return new(null, null, details.Health, unresolved);
            var ratio = Ratio(snapshot, details, issues, ruleById);
            if (ratio is null) return new(null, null, details.Health, unresolved);
            var weight = snapshot.Options.SlaSettlement == AwdSlaSettlement.TimeWeighted
                ? Math.Max(0, (index + 1 < ordered.Length ? ordered[index + 1].ObservedActiveTicks : round.ActiveEndTicks).GetValueOrDefault()
                    - check.ObservedActiveTicks.GetValueOrDefault()) : 1L;
            numerator += ratio.Value * weight;
            penalties += (AwdScoreCalculator.ExtraPenalty(effective, details.Health) ?? 0) * (AwdScoreFraction)weight;
            denominator += weight;
        }
        if (denominator == 0) return new(null, 0, null, AwdSettlementState.Exempted);
        var health = ordered.All(x => x.Evidence.Health == AwdHealth.Ok) ? AwdHealth.Ok : ordered.Last().Evidence.Health;
        return new(closed ? numerator / denominator : null, closed ? penalties / denominator : null, health,
            closed ? AwdSettlementState.Final : AwdSettlementState.Provisional,
            ordered.All(x => x.Evidence.Health == AwdHealth.Ok));
    }

    private static AwdScoreFraction? Ratio(AwdRoundRules rules, AwdFactDetails check,
        IReadOnlyDictionary<Guid, AwdFlagIssue> issues, IReadOnlyDictionary<Guid, AwdRoundRules> ruleById)
    {
        if (rules.Options.SlaBasis != AwdSlaBasis.ReadableFlags || check.Health is not (AwdHealth.Ok or AwdHealth.Recovering))
            return AwdScoreCalculator.SlaRatio(rules.Options, check.Health, check.RequiredFlags, check.ReadableFlags);
        if (!check.ManifestComplete) return null;
        var stores = rules.Stores.ToDictionary(x => x.StoreId, x => x.Weight);
        foreach (var flag in check.Flags)
        {
            if (!issues.TryGetValue(flag.FlagId, out var issue)) return null;
            // Removing a store does not erase its existing historical preservation obligations.
            if (!stores.ContainsKey(issue.StoreId))
                stores[issue.StoreId] = ruleById[issue.RulesId].Stores.Single(x => x.StoreId == issue.StoreId).Weight;
        }
        AwdScoreFraction total = 0;
        foreach (var store in stores)
        {
            var flags = check.Flags.Where(x => issues.TryGetValue(x.FlagId, out var issue) && issue.StoreId == store.Key).ToArray();
            if (flags.Any(x => x.Obligation == AwdFlagObligation.PlatformPending)) return null;
            var required = flags.Where(x => x.Obligation == AwdFlagObligation.PlayerRequired).ToArray();
            if (required.Any(x => x.Read is not (AwdProbeOutcome.Passed or AwdProbeOutcome.TargetFailure))) return null;
            var ratio = required.Length == 0 ? (AwdScoreFraction)1 : (AwdScoreFraction)required.Count(x => x.Read == AwdProbeOutcome.Passed) / required.Length;
            total += ratio * store.Value;
        }
        return stores.Count == 0 ? null : total / stores.Values.Sum();
    }

    private static AwdScoreFraction? ReadableCoverage(AwdFlagIssue issue, IReadOnlyList<AwdScoringCheck> checks,
        IReadOnlyList<AwdRound> rounds, IReadOnlyList<AwdRoundRules> rules,
        IReadOnlyDictionary<Guid, long>? enabledAt, long? stoppedActiveTicks, int currentRound)
    {
        var endTicks = Math.Min(issue.ActiveExpiryTicks ?? long.MaxValue,
            Math.Min(issue.HistoricalActiveExpiryTicks ?? long.MaxValue, stoppedActiveTicks ?? long.MaxValue));
        var endRound = Math.Min(issue.ExpiryRound ?? int.MaxValue,
            Math.Min(issue.HistoricalExpiryRound ?? int.MaxValue, checked(currentRound + 1)));
        var window = rounds.Where(x => x.Number >= issue.RoundNumber && x.Number < endRound
                && x.ActiveEndTicks > issue.ActiveStartTicks && x.ActiveStartTicks < endTicks)
            .OrderBy(x => x.Number).ToArray();
        if (window.Length == 0 || window[0].Number != issue.RoundNumber
            || window[^1].Number - window[0].Number + 1 != window.Length || window.Any(x => x.Missed)) return null;
        if (window[^1].Number < endRound - 1 && window[^1].ActiveEndTicks < endTicks) return null;
        var readable = 0;
        var required = 0;
        foreach (var round in window)
        {
            // A missing schedule is missing evidence, not a smaller denominator.
            var schedule = rules.Where(x => x.RoundId == round.Id
                    && x.CompetitionChallengeId == issue.CompetitionChallengeId && x.Teams.Any(team => team.TeamId == issue.TeamId))
                .OrderBy(x => x.Revision).FirstOrDefault();
            if (schedule is null) return null;
            // An early manual finish shortens the actual round; it must not invent a
            // replacement final sample. Persisted sample deadlines retain its original plan.
            var plannedEnd = Math.Max(round.ActiveEndTicks, checks.Where(x => x.RoundId == round.Id
                    && x.Evidence.RulesId == schedule.Id).Select(x => x.Evidence.SampleDeadlineActiveTicks ?? 0)
                .DefaultIfEmpty(0).Max());
            var plannedRound = new AwdRound { ActiveStartTicks = round.ActiveStartTicks,
                ActiveEndTicks = plannedEnd, Partial = round.Partial };
            var planned = AwdCheckPlan.Create(plannedRound, schedule, enabledAt?.GetValueOrDefault(schedule.Id) ?? round.ActiveStartTicks)
                .Where(x => x.ActiveTicks >= issue.ActiveStartTicks && x.ActiveTicks < endTicks);
            foreach (var sample in planned)
            {
                var matches = checks.Where(x => x.TeamId == issue.TeamId && x.CompetitionChallengeId == issue.CompetitionChallengeId
                    && x.RoundId == round.Id && x.Evidence.RulesId == schedule.Id
                    && x.Evidence.SampleActiveTicks == sample.ActiveTicks).ToArray();
                if (matches.Length != 1) return null;
                var evidence = matches[0].Evidence;
                if (evidence.Settlement == AwdSettlementState.Exempted) continue;
                var flag = evidence.Flags.SingleOrDefault(x => x.FlagId == issue.FlagId);
                if (evidence.Settlement != AwdSettlementState.Final || !evidence.ManifestComplete
                    || evidence.Health is null or AwdHealth.InternalError || flag is null
                    || flag.Obligation != AwdFlagObligation.PlayerRequired
                    || flag.Read is not (AwdProbeOutcome.Passed or AwdProbeOutcome.TargetFailure)) return null;
                required++;
                if (flag.Read == AwdProbeOutcome.Passed) readable++;
            }
        }
        return required == 0 ? null : (AwdScoreFraction)readable / required;
    }

    private static AwdSettlementState Merge(AwdSettlementState left, AwdSettlementState right) =>
        left == AwdSettlementState.PendingAdjudication || right == AwdSettlementState.PendingAdjudication ? AwdSettlementState.PendingAdjudication
        : left == AwdSettlementState.Provisional || right == AwdSettlementState.Provisional ? AwdSettlementState.Provisional
        : left == AwdSettlementState.Exempted && right == AwdSettlementState.Exempted ? AwdSettlementState.Exempted : AwdSettlementState.Final;

    private sealed record Availability(AwdScoreFraction? Ratio, AwdScoreFraction? Penalty, AwdHealth? Health,
        AwdSettlementState State, bool CompletelyHealthy = false);

    private sealed class ComponentAccumulator
    {
        public AwdScoreFraction Amount { get; set; }
        public bool Unknown { get; set; }
        public AwdSettlementState? State { get; set; }
        public AwdScoreComponentProjection Project(AwdScoreComponent component)
        {
            var points = Unknown ? (long?)null : Amount.Truncate();
            var raw = Unknown ? (decimal?)null : Amount.ToDecimal();
            return new(component, points, raw, raw - points, State ?? AwdSettlementState.Final);
        }
    }

    private sealed class RoundAccumulator
    {
        public Dictionary<AwdScoreComponent, ComponentAccumulator> Components { get; } = [];
        public int AttackCount { get; set; }
        public DateTimeOffset? LastAttackAt { get; set; }
        public AwdHealth? Health { get; set; }
        public bool CompleteHealthyRound { get; set; }
        public void Add(AwdScoreComponent component, AwdScoreFraction? amount, AwdSettlementState state)
        {
            if (!Components.TryGetValue(component, out var value)) Components[component] = value = new();
            value.Unknown |= amount is null;
            value.Amount += amount ?? 0;
            value.State = value.State is { } current ? Merge(current, state) : state;
        }
    }
}
