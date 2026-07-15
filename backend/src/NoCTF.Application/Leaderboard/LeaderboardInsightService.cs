using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Leaderboard;

public interface ILeaderboardInsightService
{
    Task<LeaderboardTrendResult> BuildTrendAsync(Guid competitionId, int limit = 10, CancellationToken ct = default);
    Task<LeaderboardTeamDetailResult?> BuildTeamDetailAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct = default,
        bool includeMembers = true);
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
    private const int MaxTrendTeams = 25;
    private const int MaxTrendSourceEventsPerTeam = 2048;
    private const int MaxTrendPointsPerTeam = 256;

    internal sealed record TrendScoreEvent(Guid TeamId, Guid Id, DateTime Timestamp, int PointsDelta);

    private sealed record AggregatedScoreEvent(Guid? ChallengeId, string ScoringKey, long Points);

    internal sealed record ChallengeSolveRank(Guid ChallengeId, int Rank);

    private sealed record ChallengeInfo(
        Guid Id,
        string Title,
        string TypeId,
        PointsConfig PointsConfig,
        double DifficultyCoefficient);

    public async Task<LeaderboardTrendResult> BuildTrendAsync(Guid competitionId, int limit = 10, CancellationToken ct = default)
    {
        var leaderboard = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        var topTeams = leaderboard
            .OrderBy(e => e.Rank)
            .Take(Math.Clamp(limit, 1, MaxTrendTeams))
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
            .Select(c => new { c.StartTime, c.EndTime })
            .FirstOrDefaultAsync(ct);

        var startTime = competition?.StartTime ?? DateTime.UtcNow;
        var now = DateTime.UtcNow;
        var endTime = competition?.EndTime ?? now;
        var timelineEnd = endTime <= now ? endTime : now;
        if (timelineEnd <= startTime)
            timelineEnd = startTime.AddMinutes(1);

        // Correlating each selected team with its bounded event tail translates
        // to one lateral query on PostgreSQL. This avoids one database round trip
        // per team while keeping the source history bounded independently.
        List<TrendScoreEvent> recentEvents;
        if (string.Equals(
                db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.InMemory",
                StringComparison.Ordinal))
        {
            // The EF InMemory provider does not implement correlated SelectMany.
            // Keep its test/development path single-query, then apply the same
            // per-team tail bound before building the timeline.
            recentEvents = (await BuildEligibleTrendEventsQuery(
                    db,
                    competitionId,
                    teamIds,
                    startTime,
                    timelineEnd)
                .ToListAsync(ct))
                .GroupBy(scoreEvent => scoreEvent.TeamId)
                .SelectMany(group => group
                    .OrderByDescending(scoreEvent => scoreEvent.Timestamp)
                    .ThenByDescending(scoreEvent => scoreEvent.Id)
                    .Take(MaxTrendSourceEventsPerTeam))
                .ToList();
        }
        else
        {
            recentEvents = await BuildRecentTrendEventsQuery(
                    db,
                    competitionId,
                    teamIds,
                    startTime,
                    timelineEnd)
                .ToListAsync(ct);
        }
        var eventLookup = recentEvents.ToLookup(scoreEvent => scoreEvent.TeamId);
        var eventsByTeam = teamIds.ToDictionary(
            teamId => teamId,
            teamId => (IReadOnlyList<TrendScoreEvent>)eventLookup[teamId]
                .OrderBy(scoreEvent => scoreEvent.Timestamp)
                .ThenBy(scoreEvent => scoreEvent.Id)
                .ToList());

        var series = topTeams
            .Select(team => new LeaderboardTeamSeries(
                team.TeamId,
                team.TeamName,
                BuildTrendPoints(
                    eventsByTeam.GetValueOrDefault(team.TeamId) ?? [],
                    startTime,
                    timelineEnd,
                    team.TotalScore)))
            .ToList();

        return new LeaderboardTrendResult(competitionId, series, now);
    }

    public async Task<LeaderboardTeamDetailResult?> BuildTeamDetailAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken ct = default,
        bool includeMembers = true)
    {
        var team = await db.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.CompetitionId == competitionId && t.Id == teamId)
            .Select(t => new { t.Id, t.Name, t.TrackName, t.RegistrationStatus, t.IsBanned })
            .FirstOrDefaultAsync(ct);

        if (team is null || team.RegistrationStatus != TeamRegistrationStatus.Approved || team.IsBanned)
            return null;

        var allChallenges = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == competitionId && !c.IsDeleting)
            .Select(c => new ChallengeInfo(
                c.Id,
                c.Title,
                c.TypeId,
                c.PointsConfig,
                c.DifficultyCoefficient
            ))
            .ToListAsync(ct);

        var challengeMap = allChallenges.ToDictionary(c => c.Id);
        var activeChallengeIds = challengeMap.Keys.ToArray();
        var scoreEvents = await db.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e =>
                e.CompetitionId == competitionId &&
                e.TeamId == teamId &&
                (!e.ChallengeId.HasValue || activeChallengeIds.Contains(e.ChallengeId.Value)))
            .GroupBy(e => new { e.ChallengeId, e.ScoringKey })
            .Select(group => new AggregatedScoreEvent(
                group.Key.ChallengeId,
                group.Key.ScoringKey,
                group.Sum(e => (long)e.PointsDelta)))
            .ToListAsync(ct);

        var approvedCorrectSubmissions = BuildApprovedCorrectSubmissionsQuery(
            db,
            competitionId,
            activeChallengeIds);

        // Keep the result proportional to the challenge count. The previous implementation
        // materialized one row for every (challenge, team) solve pair in the competition.
        var currentSolveCounts = await approvedCorrectSubmissions
            .GroupBy(s => s.ChallengeId)
            .Select(group => new
            {
                ChallengeId = group.Key,
                Count = group.Select(s => s.TeamId).Distinct().Count()
            })
            .ToDictionaryAsync(row => row.ChallengeId, row => row.Count, ct);

        // Compute only this team's ranks. The correlated earlier-solve count stays in SQL, so at
        // most one row per challenge solved by the requested team crosses the DB boundary.
        var challengeSolveRanks = await BuildChallengeSolveRanksQuery(
                approvedCorrectSubmissions,
                teamId)
            .ToDictionaryAsync(row => row.ChallengeId, row => row.Rank, ct);

        var teamCorrectSubmissions = await db.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.TeamId == teamId &&
                s.IsCorrect &&
                activeChallengeIds.Contains(s.ChallengeId))
            .OrderBy(s => s.SubmittedAt)
            .Select(s => new { s.UserId, s.ChallengeId, s.SubmittedAt })
            .ToListAsync(ct);

        var directionScores = scoreEvents
            .Where(e => e.ChallengeId.HasValue && challengeMap.ContainsKey(e.ChallengeId.Value))
            .GroupBy(e =>
            {
                challengeMap.TryGetValue(e.ChallengeId!.Value, out var challenge);
                return NormalizeDirection(challenge?.TypeId);
            })
            .Select(g => new LeaderboardDirectionScore(
                g.Key,
                g.Sum(e => e.Points),
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

        var scoreEventsByChallenge = scoreEvents
            .Where(e => e.ChallengeId.HasValue)
            .GroupBy(e => e.ChallengeId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        var challengeScores = allChallenges
            .OrderBy(c => NormalizeDirection(c.TypeId))
            .ThenBy(c => c.Title)
            .Select(c =>
            {
                scoreEventsByChallenge.TryGetValue(c.Id, out var challengeEvents);
                challengeEvents ??= [];
                var baseScore = challengeEvents
                    .Where(e =>
                        e.Points != 0 &&
                        !IsBonusScoringKey(e.ScoringKey))
                    .Sum(e => e.Points);
                var bonusScore = challengeEvents
                    .Where(e =>
                        e.Points != 0 &&
                        IsBonusScoringKey(e.ScoringKey))
                    .Sum(e => e.Points);
                currentSolveCounts.TryGetValue(c.Id, out var solveCount);
                var currentPoints = CtfScoreCalculator.CalculateChallengePoints(solveCount, c.PointsConfig, c.DifficultyCoefficient);
                teamSolvedAt.TryGetValue(c.Id, out var solvedAt);
                int? bloodRank = null;
                if (challengeSolveRanks.TryGetValue(c.Id, out var rank) && rank <= 3)
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

        IReadOnlyList<LeaderboardMemberHistory> members = [];
        if (includeMembers)
        {
            var users = await db.TeamMembers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(member => member.CompetitionId == competitionId && member.TeamId == teamId)
                .Join(
                    db.Users.AsNoTracking(),
                    member => member.UserId,
                    user => user.Id,
                    (_, user) => new { user.Id, user.UserName })
                .ToListAsync(ct);

            var solvesByUser = teamCorrectSubmissions
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

            members = users
                .OrderBy(u => u.UserName)
                .Select(u => new LeaderboardMemberHistory(
                    u.Id,
                    u.UserName,
                    solvesByUser.TryGetValue(u.Id, out var solves)
                        ? solves
                        : []))
                .ToList();
        }

        return new LeaderboardTeamDetailResult(
            team.Id,
            team.Name,
            team.TrackName,
            scoreEvents.Sum(e => e.Points),
            teamCorrectSubmissions.Select(s => s.ChallengeId).Distinct().Count(),
            directionScores,
            challengeScores,
            members);
    }

    private static string NormalizeDirection(string? value)
        => string.IsNullOrWhiteSpace(value) ? "GENERAL" : value.Trim().ToUpperInvariant();

    private static bool IsBonusScoringKey(string scoringKey)
        => string.Equals(scoringKey, ScoringKeys.BloodBonus, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(scoringKey, ScoringKeys.PenetrationBloodBonus, StringComparison.OrdinalIgnoreCase);

    internal static IQueryable<Submission> BuildApprovedCorrectSubmissionsQuery(
        ApplicationDbContext context,
        Guid competitionId,
        IReadOnlyCollection<Guid> activeChallengeIds)
        => context.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.IsCorrect &&
                activeChallengeIds.Contains(s.ChallengeId))
            .Join(
                context.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                submission => submission.TeamId,
                approvedTeam => approvedTeam.Id,
                (submission, _) => submission);

    internal static IQueryable<TrendScoreEvent> BuildRecentTrendEventsQuery(
        ApplicationDbContext context,
        Guid competitionId,
        IReadOnlyCollection<Guid> teamIds,
        DateTime startTime,
        DateTime timelineEnd)
    {
        var selectedTeams = context.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId &&
                teamIds.Contains(team.Id))
            .Select(team => team.Id);

        return selectedTeams.SelectMany(teamId => context.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(scoreEvent =>
                scoreEvent.CompetitionId == competitionId &&
                scoreEvent.TeamId == teamId &&
                scoreEvent.Timestamp >= startTime &&
                scoreEvent.Timestamp <= timelineEnd &&
                (!scoreEvent.ChallengeId.HasValue || context.Challenges
                    .IgnoreQueryFilters()
                    .Any(challenge =>
                        challenge.CompetitionId == competitionId &&
                        challenge.Id == scoreEvent.ChallengeId.Value &&
                        !challenge.IsDeleting)))
            .OrderByDescending(scoreEvent => scoreEvent.Timestamp)
            .ThenByDescending(scoreEvent => scoreEvent.Id)
            .Take(MaxTrendSourceEventsPerTeam)
            .Select(scoreEvent => new TrendScoreEvent(
                scoreEvent.TeamId,
                scoreEvent.Id,
                scoreEvent.Timestamp,
                scoreEvent.PointsDelta)));
    }

    private static IQueryable<TrendScoreEvent> BuildEligibleTrendEventsQuery(
        ApplicationDbContext context,
        Guid competitionId,
        IReadOnlyCollection<Guid> teamIds,
        DateTime startTime,
        DateTime timelineEnd)
        => context.ScoreEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(scoreEvent =>
                scoreEvent.CompetitionId == competitionId &&
                teamIds.Contains(scoreEvent.TeamId) &&
                scoreEvent.Timestamp >= startTime &&
                scoreEvent.Timestamp <= timelineEnd &&
                (!scoreEvent.ChallengeId.HasValue || context.Challenges
                    .IgnoreQueryFilters()
                    .Any(challenge =>
                        challenge.CompetitionId == competitionId &&
                        challenge.Id == scoreEvent.ChallengeId.Value &&
                        !challenge.IsDeleting)))
            .Select(scoreEvent => new TrendScoreEvent(
                scoreEvent.TeamId,
                scoreEvent.Id,
                scoreEvent.Timestamp,
                scoreEvent.PointsDelta));

    internal static IQueryable<ChallengeSolveRank> BuildChallengeSolveRanksQuery(
        IQueryable<Submission> approvedCorrectSubmissions,
        Guid teamId)
    {
        var targetFirstSolves = approvedCorrectSubmissions
            .Where(s => s.TeamId == teamId)
            .GroupBy(s => s.ChallengeId)
            .Select(group => new
            {
                ChallengeId = group.Key,
                SubmittedAt = group.Min(s => s.SubmittedAt)
            });

        return targetFirstSolves.Select(targetSolve => new ChallengeSolveRank(
            targetSolve.ChallengeId,
            1 + approvedCorrectSubmissions
                .Where(solve =>
                    solve.ChallengeId == targetSolve.ChallengeId &&
                    solve.SubmittedAt < targetSolve.SubmittedAt)
                .Select(solve => solve.TeamId)
                .Distinct()
                .Count()));
    }

    private static IReadOnlyList<LeaderboardTrendPoint> BuildTrendPoints(
        IReadOnlyList<TrendScoreEvent> events,
        DateTime startTime,
        DateTime timelineEnd,
        long finalScore)
    {
        // Events omitted by the bounded tail query are represented by the baseline. This
        // preserves the exact final score without loading an unbounded history.
        var visibleDelta = events.Sum(scoreEvent => (long)scoreEvent.PointsDelta);
        var total = finalScore - visibleDelta;
        var points = new List<LeaderboardTrendPoint>(MaxTrendPointsPerTeam)
        {
            new(startTime, total)
        };

        var maxEventPoints = MaxTrendPointsPerTeam - 2;
        var bucketSize = Math.Max(1, (int)Math.Ceiling(events.Count / (double)maxEventPoints));
        for (var start = 0; start < events.Count; start += bucketSize)
        {
            var end = Math.Min(start + bucketSize, events.Count);
            for (var index = start; index < end; index++)
                total += events[index].PointsDelta;

            AddOrReplaceTrendPoint(points, events[end - 1].Timestamp, total);
        }

        AddOrReplaceTrendPoint(points, timelineEnd, finalScore);
        return points;
    }

    private static void AddOrReplaceTrendPoint(
        List<LeaderboardTrendPoint> points,
        DateTime timestamp,
        long score)
    {
        if (points[^1].Timestamp == timestamp)
        {
            points[^1] = new LeaderboardTrendPoint(timestamp, score);
            return;
        }

        points.Add(new LeaderboardTrendPoint(timestamp, score));
    }
}
