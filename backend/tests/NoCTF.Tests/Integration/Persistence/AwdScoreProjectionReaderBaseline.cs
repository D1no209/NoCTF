// Full-entity oracle from commit 75f8790b. Keep independent from the optimized reader.
using NoCTF.Infrastructure.Scoring.Leaderboard;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

internal static class AwdScoreProjectionReaderBaseline
{
    public static async Task<IReadOnlyList<AwdRoundScoreProjection>> ReadAsync(NoCtfDbContext db,
        Guid competitionId, IReadOnlyList<LeaderboardChallengeFact> challenges,
        IReadOnlyList<LeaderboardTeamFact> teams, DateTimeOffset projectedAt, CancellationToken ct,
        NoCTF.Application.Competitions.Awd.AwdScoringProjectionOverride? candidate = null)
    {
        var challengeIds = challenges.Where(x => !x.IsDeleted).Select(x => x.Id).ToArray();
        var competition = await db.Competitions.AsNoTracking().SingleAsync(x => x.Id == competitionId, ct);
        var rounds = await db.AwdRounds.AsNoTracking().Where(x => x.CompetitionId == competitionId
            && x.StartsAt <= projectedAt).OrderBy(x => x.Number).ToArrayAsync(ct);
        if (rounds.Length == 0) return [];
        var roundIds = rounds.Select(x => x.Id).ToArray();
        var rules = await db.AwdRoundRules.AsNoTracking().Include(x => x.Stores).Include(x => x.Teams).AsSplitQuery()
            .Where(x => x.CompetitionId == competitionId && challengeIds.Contains(x.CompetitionChallengeId)
                && roundIds.Contains(x.RoundId) && x.EffectiveAt <= projectedAt).ToArrayAsync(ct);
        var roundNumbers = rounds.ToDictionary(round => round.Id, round => round.Number);
        var changes = await db.Set<AwdScoringRuleChange>().AsNoTracking()
            .Where(change => change.CompetitionId == competitionId && change.AppliedAt <= projectedAt)
            .OrderBy(change => change.Sequence).ToArrayAsync(ct);
        rules = rules.Select(rule =>
        {
            var number = roundNumbers[rule.RoundId];
            var change = changes.LastOrDefault(item => item.CompetitionChallengeId == rule.CompetitionChallengeId
                && number >= item.FirstRound && number <= item.LastRound);
            var policy = candidate is not null && candidate.CompetitionChallengeId == rule.CompetitionChallengeId
                && number >= candidate.FirstRound && number <= candidate.LastRound ? candidate.Policy : change?.Policy;
            return policy?.Apply(rule) ?? rule;
        }).ToArray();
        var issues = await db.AwdFlagIssues.AsNoTracking().Where(x => x.CompetitionId == competitionId
            && challengeIds.Contains(x.CompetitionChallengeId) && x.IssuedAt <= projectedAt
            && db.ChallengeFlags.Any(flag => flag.Id == x.FlagId && flag.DeletedAt == null)).ToArrayAsync(ct);
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
        var checkFacts = await db.GameplayFacts.AsNoTracking().Include(x => x.Awd!).ThenInclude(x => x.Flags).AsSplitQuery()
            .Where(x => x.CompetitionId == competitionId && x.Kind == GameplayFactKind.AwdServiceTransition
                && x.Awd != null && x.TeamId != null && x.ReferenceId != null
                && roundIds.Contains(x.ReferenceId.Value) && x.CompetitionChallengeId != null
                && challengeIds.Contains(x.CompetitionChallengeId.Value))
            .ToArrayAsync(ct);
        var clock = await AwdRunningClock.ReadAsync(db, competitionId, competition.StartAt, projectedAt, ct);
        var latest = rounds[^1];
        long ActiveAt(AwdRound round, DateTimeOffset at) => Math.Clamp(round.ActiveStartTicks
            + (clock.ElapsedAt(at) - clock.ElapsedAt(round.StartsAt)).Ticks, round.ActiveStartTicks, round.ActiveEndTicks);
        var activeTicks = ActiveAt(latest, projectedAt);
        var roundById = rounds.ToDictionary(x => x.Id);
        var checks = checkFacts.Select(fact =>
        {
            var evidence = AwdEvidenceCutoff.Read(fact.Awd!, fact.UpdatedAt, projectedAt);
            return new AwdScoringCheck(fact.TeamId!.Value, fact.CompetitionChallengeId!.Value, fact.ReferenceId!.Value,
                evidence, evidence.ObservedAt is { } at ? ActiveAt(roundById[fact.ReferenceId.Value], at) : null);
        }).ToArray();
        var deadline = await AwdCompetitionDeadline.ReadAsync(db, competitionId, competition.StartAt,
            competition.EndAt, (AwdCompetitionModeConfiguration)competition.ModeConfiguration!, projectedAt, ct);
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
