using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Leaderboard;

public interface ILeaderboardService
{
    Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(Guid competitionId, CancellationToken ct = default);
}

/// <summary>
/// Calculates the leaderboard by aggregating ScoreEvents (total score) and Submissions (solve count + first-blood).
/// Tie-breaker: equal score → earlier first correct submission ranks higher.
/// </summary>
public class LeaderboardService(ApplicationDbContext db) : ILeaderboardService
{
    public async Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
        Guid competitionId,
        CancellationToken ct = default)
    {
        // Aggregate total score per team from ScoreEvents
        // IgnoreQueryFilters: we filter by competitionId explicitly; avoids tenant context dependency
        var scores = await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(se => se.CompetitionId == competitionId)
            .GroupBy(se => se.TeamId)
            .Select(g => new { TeamId = g.Key, TotalScore = g.Sum(se => (long)se.PointsDelta) })
            .ToListAsync(ct);

        // Count distinct solved challenges per team + earliest correct submission time
        var solveStats = await db.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == competitionId && s.IsCorrect)
            .GroupBy(s => s.TeamId)
            .Select(g => new
            {
                TeamId = g.Key,
                SolvedCount = g.Select(s => s.ChallengeId).Distinct().Count(),
                FirstSolveAt = g.Min(s => s.SubmittedAt)
            })
            .ToListAsync(ct);

        // Load team names
        var teamIds = scores.Select(s => s.TeamId)
            .Union(solveStats.Select(s => s.TeamId))
            .Distinct()
            .ToList();

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId && teamIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        var teamNameMap = teams.ToDictionary(t => t.Id, t => t.Name);
        var solveMap = solveStats.ToDictionary(s => s.TeamId);

        // Build entries
        var entries = scores.Select(s =>
        {
            solveMap.TryGetValue(s.TeamId, out var stats);
            teamNameMap.TryGetValue(s.TeamId, out var name);
            return new
            {
                TeamId = s.TeamId,
                TeamName = name ?? s.TeamId.ToString(),
                TotalScore = s.TotalScore,
                SolvedCount = stats?.SolvedCount ?? 0,
                FirstSolveAt = stats?.FirstSolveAt
            };
        }).ToList();

        // Sort: descending score, then ascending first-solve time (tie-breaker)
        var sorted = entries
            .OrderByDescending(e => e.TotalScore)
            .ThenBy(e => e.FirstSolveAt ?? DateTime.MaxValue)
            .ToList();

        return sorted.Select((e, i) => new LeaderboardEntry(
            Rank: i + 1,
            TeamId: e.TeamId,
            TeamName: e.TeamName,
            TotalScore: e.TotalScore,
            SolvedCount: e.SolvedCount,
            FirstSolveAt: e.FirstSolveAt
        )).ToList();
    }
}
