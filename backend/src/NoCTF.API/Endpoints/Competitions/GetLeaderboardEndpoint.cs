using FastEndpoints;
using NoCTF.Application.Leaderboard;

namespace NoCTF.API.Endpoints.Competitions;

public class GetLeaderboardRequest
{
    public Guid CompetitionId { get; set; }
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
    public long TotalScore { get; set; }
    public int SolvedCount { get; set; }
    public DateTime? FirstSolveAt { get; set; }
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
        TotalScore = e.TotalScore,
        SolvedCount = e.SolvedCount,
        FirstSolveAt = e.FirstSolveAt
    };
}
