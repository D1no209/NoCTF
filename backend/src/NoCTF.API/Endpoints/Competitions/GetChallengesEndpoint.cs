using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetChallengesRequest
{
    public Guid Id { get; set; }
}

public class ChallengeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string TypeId { get; set; } = string.Empty;
    public int Points { get; set; }
    public int SolveCount { get; set; }
    public int? TotalStageCount { get; set; }
    public int? TotalScore { get; set; }
    public int? FullSolveCount { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public string DeploymentType { get; set; } = string.Empty;
    public int? ExposedPort { get; set; }
    public List<string> Hints { get; set; } = [];
}

/// <summary>
/// GET /api/competitions/{id}/challenges — returns all challenges for a competition.
/// </summary>
public class GetChallengesEndpoint(ApplicationDbContext dbContext) : Endpoint<GetChallengesRequest, List<ChallengeDto>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/challenges");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetChallengesRequest req, CancellationToken ct)
    {
        var challengeRows = await dbContext.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.CompetitionId == req.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                Challenge = new ChallengeDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    Description = c.Description,
                    DescriptionFormat = c.DescriptionFormat,
                    TypeId = c.TypeId,
                    Points = c.PointsConfig.InitialPoints,
                    AttachmentUrl = c.AttachmentUrl,
                    PatchTemplateUrl = c.PatchTemplateUrl,
                    DeploymentType = c.DeploymentType.ToString(),
                    ExposedPort = c.ExposedPort
                },
                c.PointsConfig,
                c.DifficultyCoefficient,
                IsPenetration = c.TypeId.ToLower() == "penetration"
            })
            .ToListAsync(ct);

        var challenges = challengeRows.Select(row => row.Challenge).ToList();
        var challengeIds = challenges.Select(c => c.Id).ToList();
        var solveCounts = await ChallengeSolveCounts.GetAsync(dbContext, req.Id, challengeIds, ct);
        var penetrationScores = await dbContext.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.CompetitionId == req.Id && challengeIds.Contains(f.ChallengeId) && f.Visible)
            .GroupBy(f => f.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, StageCount = g.Count(), TotalScore = g.Sum(f => f.Score) })
            .ToDictionaryAsync(x => x.ChallengeId, x => x, ct);
        var fullSolveCounts = await ChallengeSolveCounts.GetPenetrationFullSolveCountsAsync(dbContext, req.Id, challengeIds, ct);
        var hints = await dbContext.ChallengeHints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(h => h.CompetitionId == req.Id && challengeIds.Contains(h.ChallengeId))
            .OrderBy(h => h.DisplayOrder)
            .ToListAsync(ct);

        foreach (var challenge in challenges)
        {
            challenge.SolveCount = solveCounts.GetValueOrDefault(challenge.Id);
            var scoring = challengeRows.First(row => row.Challenge.Id == challenge.Id);
            if (scoring.IsPenetration && penetrationScores.TryGetValue(challenge.Id, out var penetrationScore))
            {
                challenge.TotalStageCount = penetrationScore.StageCount;
                challenge.TotalScore = penetrationScore.TotalScore;
                challenge.FullSolveCount = fullSolveCounts.GetValueOrDefault(challenge.Id);
                challenge.SolveCount = challenge.FullSolveCount.Value;
                challenge.Points = penetrationScore.TotalScore;
            }
            else
            {
                challenge.Points = CtfScoreCalculator.CalculateChallengePoints(
                    challenge.SolveCount,
                    scoring.PointsConfig,
                    scoring.DifficultyCoefficient);
            }
            challenge.Hints = hints
                .Where(h => h.ChallengeId == challenge.Id)
                .OrderBy(h => h.DisplayOrder)
                .Select(h => h.Content)
                .ToList();
        }

        await SendAsync(challenges, cancellation: ct);
    }
}

public static class ChallengeSolveCounts
{
    public static async Task<Dictionary<Guid, int>> GetAsync(
        ApplicationDbContext dbContext,
        Guid competitionId,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct = default)
    {
        if (challengeIds.Count == 0)
            return [];

        return await dbContext.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.IsCorrect &&
                challengeIds.Contains(s.ChallengeId))
            .Join(
                dbContext.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.ChallengeId, s.TeamId })
            .Distinct()
            .GroupBy(s => s.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, SolveCount = g.Count() })
            .ToDictionaryAsync(x => x.ChallengeId, x => x.SolveCount, ct);
    }

    public static async Task<Dictionary<Guid, int>> GetPenetrationFullSolveCountsAsync(
        ApplicationDbContext dbContext,
        Guid competitionId,
        IReadOnlyCollection<Guid> challengeIds,
        CancellationToken ct = default)
    {
        if (challengeIds.Count == 0) return [];

        var visibleCounts = await dbContext.PenetrationFlags
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.CompetitionId == competitionId && f.Visible && challengeIds.Contains(f.ChallengeId))
            .GroupBy(f => f.ChallengeId)
            .Select(g => new { ChallengeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ChallengeId, x => x.Count, ct);
        if (visibleCounts.Count == 0) return [];

        var solvedStages = await dbContext.Submissions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s =>
                s.CompetitionId == competitionId &&
                s.IsCorrect &&
                s.PenetrationFlagId != null &&
                challengeIds.Contains(s.ChallengeId))
            .Join(
                dbContext.Teams.IgnoreQueryFilters().AsNoTracking().Where(t =>
                    t.CompetitionId == competitionId &&
                    t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                    !t.IsBanned),
                s => s.TeamId,
                t => t.Id,
                (s, _) => new { s.ChallengeId, s.TeamId, FlagId = s.PenetrationFlagId!.Value })
            .Distinct()
            .GroupBy(s => new { s.ChallengeId, s.TeamId })
            .Select(g => new { g.Key.ChallengeId, g.Key.TeamId, Count = g.Count() })
            .ToListAsync(ct);

        return solvedStages
            .Where(row => visibleCounts.TryGetValue(row.ChallengeId, out var required) && row.Count >= required)
            .GroupBy(row => row.ChallengeId)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
