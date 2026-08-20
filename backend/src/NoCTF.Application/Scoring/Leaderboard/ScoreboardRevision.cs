using System.Security.Cryptography;
using System.Text;

namespace NoCTF.Application.Scoring.Leaderboard;

public static class ScoreboardRevision
{
    public static long ForSchema(
        IReadOnlyList<ScoreboardRound> rounds,
        IReadOnlyList<ScoreboardColumn> columns) => Stable(
        rounds
            .OrderBy(round => round.Number)
            .ThenBy(round => round.Id)
            .Select(round => string.Join('|',
                "round",
                round.Id.ToString("N"),
                round.Number,
                round.StartAt.UtcTicks,
                round.EndAt.UtcTicks,
                round.SettledAt?.UtcTicks,
                round.State))
            .Concat(columns
                .OrderBy(column => column.Index)
                .Select(column => string.Join('|',
                    "column",
                    column.Index,
                    column.CompetitionChallengeId.ToString("N"),
                    column.RoundId?.ToString("N")))));

    private static long Stable(IEnumerable<string> values)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return Math.Max(1, BitConverter.ToInt64(bytes, 0) & long.MaxValue);
    }
}
