using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Leaderboard;

public interface ILeaderboardService
{
    Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(Guid competitionId, CancellationToken ct = default);
}

public interface ILeaderboardProjectionBuilder
{
    Task<IReadOnlyList<ScoreboardRow>> BuildAsync(Guid competitionId, CancellationToken ct = default);
}

/// <summary>
/// Calculates the leaderboard by aggregating ScoreEvents (total score) and Submissions (solve count + first-blood).
/// Tie-breaker: equal score → earlier first correct submission ranks higher.
/// </summary>
public class LeaderboardService(ApplicationDbContext db) : ILeaderboardService, ILeaderboardProjectionBuilder
{
    public async Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
        Guid competitionId,
        CancellationToken ct = default)
    {
        var activeChallengeIds = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .Select(c => c.Id)
            .ToListAsync(ct);

        return await CalculateLeaderboardAsync(competitionId, activeChallengeIds, ct);
    }

    private async Task<IReadOnlyList<LeaderboardEntry>> CalculateLeaderboardAsync(
        Guid competitionId,
        IReadOnlyCollection<Guid> activeChallengeIds,
        CancellationToken ct)
    {

        // Aggregate total score per team from ScoreEvents
        // IgnoreQueryFilters: we filter by competitionId explicitly; avoids tenant context dependency
        var scores = await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(se =>
                se.CompetitionId == competitionId &&
                (!se.ChallengeId.HasValue || activeChallengeIds.Contains(se.ChallengeId.Value)))
            .GroupBy(se => se.TeamId)
            .Select(g => new { TeamId = g.Key, TotalScore = g.Sum(se => (long)se.PointsDelta) })
            .ToListAsync(ct);

        // Count distinct solved challenges per team + earliest correct submission time
        var solveStats = await db.Submissions
            .IgnoreQueryFilters()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.IsCorrect &&
                activeChallengeIds.Contains(s.ChallengeId))
            .GroupBy(s => s.TeamId)
            .Select(g => new
            {
                TeamId = g.Key,
                SolvedCount = g.Select(s => s.ChallengeId).Distinct().Count(),
                FirstSolveAt = g.Min(s => s.SubmittedAt)
            })
            .ToListAsync(ct);

        var teams = await db.Teams
            .IgnoreQueryFilters()
            .Where(t =>
                t.CompetitionId == competitionId &&
                !t.IsBanned &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved)
            .Select(t => new { t.Id, t.Name, t.TrackName })
            .ToListAsync(ct);

        var scoreMap = scores.ToDictionary(s => s.TeamId, s => s.TotalScore);
        var solveMap = solveStats.ToDictionary(s => s.TeamId);

        // Build entries
        var entries = teams.Select(t =>
        {
            scoreMap.TryGetValue(t.Id, out var totalScore);
            solveMap.TryGetValue(t.Id, out var stats);
            return new LeaderboardEntry(
                Rank: 0,
                TeamId: t.Id,
                TeamName: t.Name,
                TrackName: t.TrackName,
                TotalScore: totalScore,
                SolvedCount: stats?.SolvedCount ?? 0,
                FirstSolveAt: stats?.FirstSolveAt);
        }).ToList();

        return LeaderboardOrdering.SortAndRank(entries);
    }

    public async Task<IReadOnlyList<ScoreboardRow>> BuildAsync(Guid competitionId, CancellationToken ct = default)
    {
        var activeChallengeIds = await db.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .Select(c => c.Id)
            .ToListAsync(ct);
        var entries = await CalculateLeaderboardAsync(competitionId, activeChallengeIds, ct);
        var activeTeamIds = entries.Select(e => e.TeamId).ToList();
        var scoreEvents = await db.ScoreEvents
            .IgnoreQueryFilters()
            .Where(e =>
                e.CompetitionId == competitionId &&
                activeTeamIds.Contains(e.TeamId) &&
                (!e.ChallengeId.HasValue || activeChallengeIds.Contains(e.ChallengeId.Value)))
            .GroupBy(e => new { e.TeamId, e.ScoringKey })
            .Select(g => new { g.Key.TeamId, g.Key.ScoringKey, Points = g.Sum(e => (long)e.PointsDelta) })
            .ToListAsync(ct);

        var metricsByTeam = scoreEvents
            .GroupBy(e => e.TeamId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, long>)g.ToDictionary(
                    e => string.IsNullOrWhiteSpace(e.ScoringKey) ? "legacy" : e.ScoringKey,
                    e => e.Points,
                    StringComparer.OrdinalIgnoreCase));

        return entries.Select(e =>
        {
            metricsByTeam.TryGetValue(e.TeamId, out var metrics);
            metrics ??= new Dictionary<string, long>();
            return new ScoreboardRow(
                e.Rank,
                e.TeamId,
                e.TeamName,
                e.TotalScore,
                metrics,
                new Dictionary<string, string>
                {
                    ["solvedCount"] = e.SolvedCount.ToString(),
                    ["firstSolveAt"] = e.FirstSolveAt?.ToString("O") ?? string.Empty
                });
        }).ToList();
    }
}

internal static class LeaderboardOrdering
{
    public static IReadOnlyList<LeaderboardEntry> SortAndRank(
        IEnumerable<LeaderboardEntry> entries)
        => entries
            .OrderByDescending(entry => entry.TotalScore)
            .ThenByDescending(entry => entry.SolvedCount)
            .ThenBy(entry => entry.FirstSolveAt is null)
            .ThenBy(entry => entry.FirstSolveAt)
            .ThenBy(entry => entry.TeamName, StringComparer.Ordinal)
            .Select((entry, index) => entry with { Rank = index + 1 })
            .ToList();
}
