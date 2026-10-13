using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.WriteUps;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.GameModes.Awd.Scoring;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Scoring.Leaderboard;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal static class AwdSettledRankingReader
{
    public static async Task<IReadOnlyDictionary<Guid, int>> ReadAsync(NoCtfDbContext db, Competition competition,
        int beforeRound, IReadOnlyList<Guid> eligibleTeams, DateTimeOffset now, CancellationToken ct)
    {
        if (beforeRound <= 1) return new Dictionary<Guid, int>();
        var teams = await db.Teams.AsNoTracking().Where(x => eligibleTeams.Contains(x.Id))
            .Select(x => new LeaderboardTeamFact(x.Id, x.Name, x.IsBanned, x.DeletedAt != null,
                x.RegisteredAt, x.TrackKey, true, false, false, true, true)).ToArrayAsync(ct);
        var challenges = await db.CompetitionChallenges.AsNoTracking().Where(x => x.CompetitionId == competition.Id)
            .Select(x => new LeaderboardChallengeFact(x.Id, string.Empty, string.Empty, x.DeletedAt != null,
                null, x.Order, x.IsPublished, null, CtfInteractionKind.FlagSubmission, null,
                new ChallengeTiming(x.AutoOpenAt, x.ScoringEndsAt, x.SubmissionDeadlineAt))).ToArrayAsync(ct);
        var scores = (await AwdScoreProjectionReader.ReadAsync(db, competition.Id, challenges, teams, now, ct))
            .Where(x => x.RoundNumber < beforeRound).ToArray();
        if (scores.Length == 0) return new Dictionary<Guid, int>();
        // Only a fully settled prefix is a ranking snapshot. Never use a partially computed current board.
        var firstUnsettled = scores.Where(x => x.Components.Any(component => component.Settlement is
                AwdSettlementState.Provisional or AwdSettlementState.PendingAdjudication))
            .Select(x => x.RoundNumber).DefaultIfEmpty(beforeRound).Min();
        var settled = scores.Where(x => x.RoundNumber < firstUnsettled).ToArray();
        if (settled.Length == 0) return new Dictionary<Guid, int>();
        var lastRound = settled.Max(x => x.RoundNumber);
        var settledRound = await db.AwdRounds.AsNoTracking()
            .SingleAsync(x => x.CompetitionId == competition.Id && x.Number == lastRound, ct);
        var cutoff = settledRound.EndsAt;
        var tieBreaks = AwdTieBreakPolicy.Resolve(settledRound.TieBreakers.OrderBy(item => item.Position)
            .Select(item => new AwdTieBreakCriterion(item.Field, item.Direction)));
        var manuals = await db.GameplayFacts.AsNoTracking().Where(x => x.CompetitionId == competition.Id
            && x.Kind == GameplayFactKind.ManualAdjustment && x.Result == GameplayFactResult.Applied
            && x.OccurredAt < cutoff && x.TeamId != null).Select(x => new { TeamId = x.TeamId!.Value, x.Value }).ToArrayAsync(ct);
        var unlocks = await db.WriteUpUnlockReceipts.AsNoTracking().Where(x => x.CompetitionId == competition.Id
            && x.UnlockedAt <= cutoff).ToArrayAsync(ct);
        var floor = await db.AwdRoundRules.Where(x => x.CompetitionId == competition.Id && x.EffectiveAt < cutoff)
            .OrderByDescending(x => x.EffectiveAt).Select(x => x.Options.MinimumTotalScore).FirstOrDefaultAsync(ct);
        var byTeam = teams.ToDictionary(x => x.Id);
        var totals = AwdScoreProjectionReader.Aggregate(settled).GroupBy(x => x.TeamId).Select(group =>
        {
            var score = checked(group.Sum(x => x.Score) + manuals.Where(x => x.TeamId == group.Key).Sum(x =>
                (long)int.Parse(x.Value!, System.Globalization.CultureInfo.InvariantCulture)));
            var attack = group.Sum(x => x.AttackPoints);
            foreach (var value in group)
            {
                var percent = unlocks.SingleOrDefault(x => x.TeamId == group.Key
                    && x.CompetitionChallengeId == value.CompetitionChallengeId)?.DeductionPercent ?? 0;
                score = checked(score - ChallengeWriteUpPolicy.Deduction(value.PositivePoints, percent));
                attack = checked(attack - ChallengeWriteUpPolicy.Deduction(value.AttackPoints, percent));
            }
            return new { Team = group.Key, Score = floor is { } minimum ? Math.Max(minimum, score) : score,
                Attack = attack, Healthy = group.Sum(x => x.UpRoundCount), Captures = group.Sum(x => x.AttackCount),
                Last = group.Max(x => x.LastAttackAt), byTeam[group.Key].RegisteredAt };
        });
        var ranked = AwdTeamRanking.Order(totals, tieBreaks, value => new(value.Team, value.Score,
            value.Attack, value.Healthy, value.Captures, value.Last, value.RegisteredAt)).ToArray();
        return ranked.Select((value, index) => (value.Team, Rank: index + 1)).ToDictionary(x => x.Team, x => x.Rank);
    }
}
