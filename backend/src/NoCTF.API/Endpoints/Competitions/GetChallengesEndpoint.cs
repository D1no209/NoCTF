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
    public string? AttachmentUrl { get; set; }
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
                    DeploymentType = c.DeploymentType.ToString(),
                    ExposedPort = c.ExposedPort
                },
                c.PointsConfig,
                c.DifficultyCoefficient
            })
            .ToListAsync(ct);

        var challenges = challengeRows.Select(row => row.Challenge).ToList();
        var challengeIds = challenges.Select(c => c.Id).ToList();
        var solveCounts = await ChallengeSolveCounts.GetAsync(dbContext, req.Id, challengeIds, ct);
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
            challenge.Points = CtfScoreCalculator.CalculateChallengePoints(
                challenge.SolveCount,
                scoring.PointsConfig,
                scoring.DifficultyCoefficient);
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
}
