using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Leaderboard;

public interface ILeaderboardInsightService
{
    Task<LeaderboardTrendResult> BuildTrendAsync(Guid competitionId, int limit = 10, CancellationToken ct = default);
    Task<LeaderboardTeamDetailResult?> BuildTeamDetailAsync(Guid competitionId, Guid teamId, CancellationToken ct = default);
}

public sealed record LeaderboardTrendPoint(DateTime Timestamp, long Score);

public sealed record LeaderboardTeamSeries(
    Guid TeamId,
    string TeamName,
    IReadOnlyList<LeaderboardTrendPoint> Points);

public sealed record LeaderboardTrendResult(
    Guid CompetitionId,
    IReadOnlyList<LeaderboardTeamSeries> Series,
    DateTime GeneratedAt);

public sealed record LeaderboardDirectionScore(
    string Direction,
    long Score,
    int SolvedCount);

public sealed record LeaderboardMemberSolve(
    Guid ChallengeId,
    string ChallengeTitle,
    string Direction,
    DateTime SubmittedAt);

public sealed record LeaderboardMemberHistory(
    Guid UserId,
    string UserName,
    IReadOnlyList<LeaderboardMemberSolve> Solves);

public sealed record LeaderboardTeamDetailResult(
    Guid TeamId,
    string TeamName,
    string? TrackName,
    long TotalScore,
    int SolvedCount,
    IReadOnlyList<LeaderboardDirectionScore> DirectionScores,
    IReadOnlyList<LeaderboardChallengeScore> ChallengeScores,
    IReadOnlyList<LeaderboardMemberHistory> Members);

public sealed record LeaderboardChallengeScore(
    Guid ChallengeId,
    string ChallengeTitle,
    string Direction,
    int CurrentPoints,
    long BaseScore,
    long BonusScore,
    long TotalScore,
    int? BloodRank,
    DateTime? SolvedAt);

public class LeaderboardInsightService(
    ApplicationDbContext db,
    ILeaderboardService leaderboardService) : ILeaderboardInsightService
{
    public async Task<LeaderboardTrendResult> BuildTrendAsync(Guid competitionId, int limit = 10, CancellationToken ct = default)
    {
        var leaderboard = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        var topTeams = leaderboard
            .OrderBy(e => e.Rank)
            .Take(Math.Max(1, limit))
            .ToList();

        if (topTeams.Count == 0)
        {
            return new LeaderboardTrendResult(competitionId, [], DateTime.UtcNow);
        }

        var teamIds = topTeams.Select(t => t.TeamId).ToHashSet();
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.StartTime })
            .FirstOrDefaultAsync(ct);

        var startTime = competition?.StartTime ?? DateTime.UtcNow;
        var events = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.CompetitionId == competitionId && teamIds.Contains(e.TeamId))
            .OrderBy(e => e.Timestamp)
            .Select(e => new { e.TeamId, e.Timestamp, e.PointsDelta })
            .ToListAsync(ct);

        var eventsByTeam = events.GroupBy(e => e.TeamId).ToDictionary(g => g.Key, g => g.ToList());
        var series = topTeams.Select(team =>
        {
            long total = 0;
            var points = new List<LeaderboardTrendPoint> { new(startTime, 0) };

            if (eventsByTeam.TryGetValue(team.TeamId, out var teamEvents))
            {
                foreach (var scoreEvent in teamEvents)
                {
                    total += scoreEvent.PointsDelta;
                    points.Add(new LeaderboardTrendPoint(scoreEvent.Timestamp, total));
                }
            }

            if (points.Count == 1 || points[^1].Score != team.TotalScore)
                points.Add(new LeaderboardTrendPoint(DateTime.UtcNow, team.TotalScore));

            return new LeaderboardTeamSeries(team.TeamId, team.TeamName, points);
        }).ToList();

        return new LeaderboardTrendResult(competitionId, series, DateTime.UtcNow);
    }

    public async Task<LeaderboardTeamDetailResult?> BuildTeamDetailAsync(Guid competitionId, Guid teamId, CancellationToken ct = default)
    {
        var team = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.CompetitionId == competitionId && t.Id == teamId)
            .Select(t => new { t.Id, t.Name, t.TrackName, t.RegistrationStatus })
            .FirstOrDefaultAsync(ct);

        if (team is null || team.RegistrationStatus == TeamRegistrationStatus.Rejected)
            return null;

        var scoreEvents = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.CompetitionId == competitionId && e.TeamId == teamId)
            .Select(e => new { e.ChallengeId, e.PointsDelta, e.ScoringKey })
            .ToListAsync(ct);

        var allChallenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.TypeId,
                c.PointsConfig,
                c.DifficultyCoefficient
            })
            .ToListAsync(ct);

        var challengeMap = allChallenges.ToDictionary(c => c.Id);
        var correctSubmissions = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => s)
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new { s.TeamId, s.UserId, s.ChallengeId, s.SubmittedAt })
            .ToListAsync(ct);

        var teamCorrectSubmissions = correctSubmissions
            .Where(s => s.TeamId == teamId)
            .ToList();

        var challengeSolveRanks = correctSubmissions
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .GroupBy(s => s.TeamId)
                    .Select(teamGroup => new { TeamId = teamGroup.Key, SubmittedAt = teamGroup.Min(s => s.SubmittedAt) })
                    .OrderBy(s => s.SubmittedAt)
                    .Select((solve, index) => new { solve.TeamId, Rank = index + 1 })
                    .ToDictionary(s => s.TeamId, s => s.Rank));

        var currentSolveCounts = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CompetitionId == competitionId && s.IsCorrect)
            .Join(
                db.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.ChallengeId, s.TeamId })
            .Distinct()
            .GroupBy(s => s.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ChallengeId, g => g.Count, ct);

        var directionScores = scoreEvents
            .GroupBy(e =>
            {
                if (e.ChallengeId.HasValue && challengeMap.TryGetValue(e.ChallengeId.Value, out var challenge))
                    return NormalizeDirection(challenge.TypeId);
                return "GENERAL";
            })
            .Select(g => new LeaderboardDirectionScore(
                g.Key,
                g.Sum(e => (long)e.PointsDelta),
                teamCorrectSubmissions
                    .Where(s => challengeMap.TryGetValue(s.ChallengeId, out var c) && NormalizeDirection(c.TypeId) == g.Key)
                    .Select(s => s.ChallengeId)
                    .Distinct()
                    .Count()))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Direction)
            .ToList();

        var teamSolvedAt = teamCorrectSubmissions
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => (DateTime?)g.Min(s => s.SubmittedAt));

        var challengeScores = allChallenges
            .OrderBy(c => NormalizeDirection(c.TypeId))
            .ThenBy(c => c.Title)
            .Select(c =>
            {
                var baseScore = scoreEvents
                    .Where(e => e.ChallengeId == c.Id && e.PointsDelta != 0 && e.ScoringKey == ScoringKeys.DecaySolve)
                    .Sum(e => (long)e.PointsDelta);
                var bonusScore = scoreEvents
                    .Where(e => e.ChallengeId == c.Id && e.PointsDelta != 0 && e.ScoringKey == ScoringKeys.BloodBonus)
                    .Sum(e => (long)e.PointsDelta);
                currentSolveCounts.TryGetValue(c.Id, out var solveCount);
                var currentPoints = CtfScoreCalculator.CalculateChallengePoints(solveCount, c.PointsConfig, c.DifficultyCoefficient);
                teamSolvedAt.TryGetValue(c.Id, out var solvedAt);
                int? bloodRank = null;
                if (challengeSolveRanks.TryGetValue(c.Id, out var ranks) && ranks.TryGetValue(teamId, out var rank) && rank <= 3)
                    bloodRank = rank;

                return new LeaderboardChallengeScore(
                    c.Id,
                    c.Title,
                    NormalizeDirection(c.TypeId),
                    currentPoints,
                    baseScore,
                    bonusScore,
                    baseScore + bonusScore,
                    bloodRank,
                    solvedAt);
            })
            .ToList();

        var memberIds = await db.TeamMembers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.CompetitionId == competitionId && m.TeamId == teamId)
            .Select(m => m.UserId)
            .ToListAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Where(u => memberIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName })
            .ToListAsync(ct);

        var solvesByUser = correctSubmissions
            .GroupBy(s => s.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s =>
                {
                    challengeMap.TryGetValue(s.ChallengeId, out var challenge);
                    return new LeaderboardMemberSolve(
                        s.ChallengeId,
                        challenge?.Title ?? s.ChallengeId.ToString(),
                        NormalizeDirection(challenge?.TypeId),
                        s.SubmittedAt);
                }).ToList());

        var members = users
            .OrderBy(u => u.UserName)
            .Select(u => new LeaderboardMemberHistory(
                u.Id,
                u.UserName,
                solvesByUser.TryGetValue(u.Id, out var solves)
                    ? solves
                    : []))
            .ToList();

        var entry = (await leaderboardService.CalculateLeaderboardAsync(competitionId, ct))
            .FirstOrDefault(e => e.TeamId == teamId);

        return new LeaderboardTeamDetailResult(
            team.Id,
            team.Name,
            team.TrackName,
            entry?.TotalScore ?? scoreEvents.Sum(e => (long)e.PointsDelta),
            entry?.SolvedCount ?? teamCorrectSubmissions.Select(s => s.ChallengeId).Distinct().Count(),
            directionScores,
            challengeScores,
            members);
    }

    private static string NormalizeDirection(string? value)
        => string.IsNullOrWhiteSpace(value) ? "GENERAL" : value.Trim().ToUpperInvariant();
}
