using FastEndpoints;
using NoCTF.Application.Leaderboard;

namespace NoCTF.API.Endpoints.Competitions;

public class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
}

public class GetLeaderboardTrendRequest
{
    public Guid CompetitionId { get; set; }
}

public class GetLeaderboardTeamDetailRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public class GetLeaderboardResponse
{
    public Guid CompetitionId { get; set; }
    public List<LeaderboardEntryDto> Entries { get; set; } = [];
    public bool FromCache { get; set; }
}

public class LeaderboardEntryDto
{
    public int Rank { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? TrackName { get; set; }
    public long TotalScore { get; set; }
    public int SolvedCount { get; set; }
    public DateTime? FirstSolveAt { get; set; }
}

public class LeaderboardTrendResponse
{
    public Guid CompetitionId { get; set; }
    public List<LeaderboardTeamSeriesDto> Series { get; set; } = [];
    public DateTime GeneratedAt { get; set; }
}

public class LeaderboardTeamSeriesDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public List<LeaderboardTrendPointDto> Points { get; set; } = [];
}

public class LeaderboardTrendPointDto
{
    public DateTime Timestamp { get; set; }
    public long Score { get; set; }
}

public class LeaderboardTeamDetailDto
{
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string? TrackName { get; set; }
    public long TotalScore { get; set; }
    public int SolvedCount { get; set; }
    public List<LeaderboardDirectionScoreDto> DirectionScores { get; set; } = [];
    public List<LeaderboardChallengeScoreDto> ChallengeScores { get; set; } = [];
    public List<LeaderboardMemberHistoryDto> Members { get; set; } = [];
}

public class LeaderboardChallengeScoreDto
{
    public Guid ChallengeId { get; set; }
    public string ChallengeTitle { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int CurrentPoints { get; set; }
    public long BaseScore { get; set; }
    public long BonusScore { get; set; }
    public long TotalScore { get; set; }
    public int? BloodRank { get; set; }
    public DateTime? SolvedAt { get; set; }
}

public class LeaderboardDirectionScoreDto
{
    public string Direction { get; set; } = string.Empty;
    public long Score { get; set; }
    public int SolvedCount { get; set; }
}

public class LeaderboardMemberHistoryDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public List<LeaderboardMemberSolveDto> Solves { get; set; } = [];
}

public class LeaderboardMemberSolveDto
{
    public Guid ChallengeId { get; set; }
    public string ChallengeTitle { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

/// <summary>
/// GET /api/competitions/{competitionId}/leaderboard
/// Reads from Redis cache first; falls back to LeaderboardService on cache miss.
/// </summary>
public class GetLeaderboardEndpoint(
    IRedisLeaderboardCache leaderboardCache,
    ILeaderboardService leaderboardService)
    : Endpoint<GetLeaderboardRequest, GetLeaderboardResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetLeaderboardRequest req, CancellationToken ct)
    {
        // Try Redis cache first
        var cached = await leaderboardCache.GetAsync(req.CompetitionId, ct);
        if (cached is not null)
        {
            await SendAsync(new GetLeaderboardResponse
            {
                CompetitionId = req.CompetitionId,
                Entries = cached.Select(MapToDto).ToList(),
                FromCache = true
            }, cancellation: ct);
            return;
        }

        // Cache miss — calculate from DB
        var entries = await leaderboardService.CalculateLeaderboardAsync(req.CompetitionId, ct);

        // Populate cache for next request (fire-and-forget, don't block response)
        _ = leaderboardCache.UpdateAsync(req.CompetitionId, entries, CancellationToken.None);

        await SendAsync(new GetLeaderboardResponse
        {
            CompetitionId = req.CompetitionId,
            Entries = entries.Select(MapToDto).ToList(),
            FromCache = false
        }, cancellation: ct);
    }

    private static LeaderboardEntryDto MapToDto(LeaderboardEntry e) => new()
    {
        Rank = e.Rank,
        TeamId = e.TeamId,
        TeamName = e.TeamName,
        TrackName = e.TrackName,
        TotalScore = e.TotalScore,
        SolvedCount = e.SolvedCount,
        FirstSolveAt = e.FirstSolveAt
    };
}

public class GetLeaderboardTrendEndpoint(ILeaderboardInsightService insightService)
    : Endpoint<GetLeaderboardTrendRequest, LeaderboardTrendResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard/trend");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetLeaderboardTrendRequest req, CancellationToken ct)
    {
        var result = await insightService.BuildTrendAsync(req.CompetitionId, 10, ct);
        await SendAsync(new LeaderboardTrendResponse
        {
            CompetitionId = result.CompetitionId,
            GeneratedAt = result.GeneratedAt,
            Series = result.Series.Select(s => new LeaderboardTeamSeriesDto
            {
                TeamId = s.TeamId,
                TeamName = s.TeamName,
                Points = s.Points.Select(p => new LeaderboardTrendPointDto
                {
                    Timestamp = p.Timestamp,
                    Score = p.Score
                }).ToList()
            }).ToList()
        }, cancellation: ct);
    }
}

public class GetLeaderboardTeamDetailEndpoint(ILeaderboardInsightService insightService)
    : Endpoint<GetLeaderboardTeamDetailRequest, LeaderboardTeamDetailDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard/teams/{teamId}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetLeaderboardTeamDetailRequest req, CancellationToken ct)
    {
        var result = await insightService.BuildTeamDetailAsync(req.CompetitionId, req.TeamId, ct);
        if (result is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new LeaderboardTeamDetailDto
        {
            TeamId = result.TeamId,
            TeamName = result.TeamName,
            TrackName = result.TrackName,
            TotalScore = result.TotalScore,
            SolvedCount = result.SolvedCount,
            DirectionScores = result.DirectionScores.Select(s => new LeaderboardDirectionScoreDto
            {
                Direction = s.Direction,
                Score = s.Score,
                SolvedCount = s.SolvedCount
            }).ToList(),
            ChallengeScores = result.ChallengeScores.Select(s => new LeaderboardChallengeScoreDto
            {
                ChallengeId = s.ChallengeId,
                ChallengeTitle = s.ChallengeTitle,
                Direction = s.Direction,
                CurrentPoints = s.CurrentPoints,
                BaseScore = s.BaseScore,
                BonusScore = s.BonusScore,
                TotalScore = s.TotalScore,
                BloodRank = s.BloodRank,
                SolvedAt = s.SolvedAt
            }).ToList(),
            Members = result.Members.Select(m => new LeaderboardMemberHistoryDto
            {
                UserId = m.UserId,
                UserName = m.UserName,
                Solves = m.Solves.Select(s => new LeaderboardMemberSolveDto
                {
                    ChallengeId = s.ChallengeId,
                    ChallengeTitle = s.ChallengeTitle,
                    Direction = s.Direction,
                    SubmittedAt = s.SubmittedAt
                }).ToList()
            }).ToList()
        }, cancellation: ct);
    }
}
