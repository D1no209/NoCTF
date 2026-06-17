using FastEndpoints;
using Microsoft.EntityFrameworkCore;
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
    public string TypeId { get; set; } = string.Empty;
    public int Points { get; set; }
    public int SolveCount { get; set; }
    public string? AttachmentUrl { get; set; }
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
        var challenges = await dbContext.Challenges
            .IgnoreQueryFilters()
            .Where(c => c.CompetitionId == req.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ChallengeDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                TypeId = c.TypeId,
                Points = c.PointsConfig.InitialPoints,
                SolveCount = dbContext.Submissions.Count(s => s.CompetitionId == req.Id && s.ChallengeId == c.Id && s.IsCorrect),
                AttachmentUrl = c.AttachmentUrl
            })
            .ToListAsync(ct);

        await SendAsync(challenges, cancellation: ct);
    }
}
