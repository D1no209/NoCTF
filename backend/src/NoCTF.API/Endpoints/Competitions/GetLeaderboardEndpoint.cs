using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Leaderboard;
using NoCTF.Core;
using NoCTF.Infrastructure;

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
    ILeaderboardService leaderboardService,
    ApplicationDbContext db,
    ILogger<GetLeaderboardEndpoint> logger)
    : Endpoint<GetLeaderboardRequest, GetLeaderboardResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetLeaderboardRequest req, CancellationToken ct)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.CompetitionId)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);
        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new GetLeaderboardResponse
            {
                CompetitionId = req.CompetitionId,
                Entries = [],
                FromCache = false
            }, cancellation: ct);
            return;
        }

        // Try Redis cache first
        IReadOnlyList<LeaderboardEntry>? cached = null;
        try
        {
            cached = await leaderboardCache.GetAsync(req.CompetitionId, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // The cache is an acceleration layer for this read. Keep the public
            // scoreboard available from PostgreSQL during a transient Redis outage.
            logger.LogWarning(
                exception,
                "Leaderboard cache read failed for competition {CompetitionId}; using the database projection.",
                req.CompetitionId);
        }
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

        // Reserve before reading PostgreSQL. If a mutation invalidates or
        // refreshes the cache while this projection is running, its newer
        // version must win even when this older calculation finishes last.
        long? cacheVersion = null;
        try
        {
            cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(req.CompetitionId, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Leaderboard cache version reservation failed for competition {CompetitionId}; returning the database projection.",
                req.CompetitionId);
        }

        // Cache miss — calculate from DB
        var entries = await leaderboardService.CalculateLeaderboardAsync(req.CompetitionId, ct);

        try
        {
            if (cacheVersion.HasValue)
                await leaderboardCache.UpdateAsync(req.CompetitionId, entries, cacheVersion.Value, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Leaderboard cache update failed for competition {CompetitionId}; returning the database projection.",
                req.CompetitionId);
        }

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

public class GetLeaderboardTrendEndpoint(ILeaderboardInsightService insightService, ApplicationDbContext db)
    : Endpoint<GetLeaderboardTrendRequest, LeaderboardTrendResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard/trend");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetLeaderboardTrendRequest req, CancellationToken ct)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.CompetitionId)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);
        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new LeaderboardTrendResponse
            {
                CompetitionId = req.CompetitionId,
                GeneratedAt = DateTime.UtcNow,
                Series = []
            }, cancellation: ct);
            return;
        }

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

public class GetLeaderboardTeamDetailEndpoint(ILeaderboardInsightService insightService, ApplicationDbContext db)
    : Endpoint<GetLeaderboardTeamDetailRequest, LeaderboardTeamDetailDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{competitionId}/leaderboard/teams/{teamId}");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetLeaderboardTeamDetailRequest req, CancellationToken ct)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.CompetitionId)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);
        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var canViewMembers = User.IsInRole(UserRole.Admin.ToString());
        var userIdValue = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!canViewMembers && Guid.TryParse(userIdValue, out var userId))
        {
            var memberAccess = db.TeamMembers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(member =>
                    member.CompetitionId == req.CompetitionId &&
                    member.TeamId == req.TeamId &&
                    member.UserId == userId)
                .Select(_ => 1);
            var ownerAccess = db.Competitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == req.CompetitionId && c.OwnerId == userId)
                .Select(_ => 1);
            var managerAccess = db.CompetitionCollaborators
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(collaborator =>
                    collaborator.CompetitionId == req.CompetitionId &&
                    collaborator.UserId == userId &&
                    collaborator.Role == CollaboratorRole.Manager)
                .Select(_ => 1);
            canViewMembers = await memberAccess
                .Concat(ownerAccess)
                .Concat(managerAccess)
                .AnyAsync(ct);
        }

        var result = await insightService.BuildTeamDetailAsync(
            req.CompetitionId,
            req.TeamId,
            ct,
            includeMembers: canViewMembers);
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
            Members = canViewMembers ? result.Members.Select(m => new LeaderboardMemberHistoryDto
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
            }).ToList() : []
        }, cancellation: ct);
    }
}
