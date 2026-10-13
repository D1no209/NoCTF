using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;

namespace NoCTF.GameModes.Awd.Scoring;

public readonly record struct AwdRankingValues(Guid TeamId, long TotalScore, long AttackPoints,
    int HealthyRounds, int AttackCount, DateTimeOffset? LastAttackAt, DateTimeOffset RegisteredAt);

public static class AwdTeamRanking
{
    public static IOrderedEnumerable<T> Order<T>(IEnumerable<T> teams, LeaderboardProjectionInput input,
        Func<T, AwdRankingValues> values)
    {
        var rules = input.AwdTieBreakers ?? AwdTieBreakPolicy.Resolve(
            (input.CompetitionConfiguration as AwdCompetitionModeConfiguration)?.TieBreakers
                .OrderBy(item => item.Position).Select(item => new AwdTieBreakCriterion(item.Field, item.Direction)) ?? []);
        var ordered = teams.OrderByDescending(team => values(team).TotalScore);
        foreach (var rule in rules)
        {
            var ascending = rule.Direction == AwdSortDirection.Ascending;
            ordered = rule.Field switch
            {
                AwdTieBreakField.AttackPoints => ascending ? ordered.ThenBy(team => values(team).AttackPoints)
                    : ordered.ThenByDescending(team => values(team).AttackPoints),
                AwdTieBreakField.HealthyRounds => ascending ? ordered.ThenBy(team => values(team).HealthyRounds)
                    : ordered.ThenByDescending(team => values(team).HealthyRounds),
                AwdTieBreakField.AttackCount => ascending ? ordered.ThenBy(team => values(team).AttackCount)
                    : ordered.ThenByDescending(team => values(team).AttackCount),
                // No accepted attack is always last; it cannot win by a fabricated timestamp.
                AwdTieBreakField.LastAttackAt => ascending ? ordered.ThenBy(team => values(team).LastAttackAt ?? DateTimeOffset.MaxValue)
                    : ordered.ThenByDescending(team => values(team).LastAttackAt ?? DateTimeOffset.MinValue),
                AwdTieBreakField.RegistrationTime => ascending ? ordered.ThenBy(team => values(team).RegisteredAt)
                    : ordered.ThenByDescending(team => values(team).RegisteredAt),
                _ => throw new InvalidOperationException("Unsupported AWD tie-break field.")
            };
        }
        return ordered.ThenBy(team => values(team).TeamId);
    }
}
