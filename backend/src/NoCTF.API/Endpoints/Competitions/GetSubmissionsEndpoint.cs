using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetSubmissionsRequest
{
    public Guid Id { get; set; }
}

public class SolvedChallengeDto
{
    public Guid ChallengeId { get; set; }
    public DateTime SolvedAt { get; set; }
}

public class GetSubmissionsResponse
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public List<SolvedChallengeDto> SolvedChallenges { get; set; } = [];
}

/// <summary>
/// GET /api/competitions/{id}/submissions — returns the authenticated user's team correct submissions.
/// </summary>
public class GetSubmissionsEndpoint(ApplicationDbContext dbContext) : Endpoint<GetSubmissionsRequest, GetSubmissionsResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/submissions");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(GetSubmissionsRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        // Find the user's team in this competition
        var teamMember = await dbContext.TeamMembers
            .AsNoTracking()
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == req.Id),
                  tm => tm.TeamId,
                  t => t.Id,
                  (tm, t) => new { tm.UserId, t.Id, t.CompetitionId })
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (teamMember is null)
        {
            // User has no team in this competition — return empty
            await SendAsync(new GetSubmissionsResponse
            {
                CompetitionId = req.Id,
                TeamId = Guid.Empty,
                SolvedChallenges = []
            }, cancellation: ct);
            return;
        }

        var solvedChallenges = await dbContext.Submissions
            .IgnoreQueryFilters()
            .Where(s => s.CompetitionId == req.Id
                     && s.TeamId == teamMember.Id
                     && s.IsCorrect)
            .GroupBy(s => s.ChallengeId)
            .Select(g => new SolvedChallengeDto
            {
                ChallengeId = g.Key,
                SolvedAt = g.Min(s => s.SubmittedAt)
            })
            .ToListAsync(ct);

        await SendAsync(new GetSubmissionsResponse
        {
            CompetitionId = req.Id,
            TeamId = teamMember.Id,
            SolvedChallenges = solvedChallenges
        }, cancellation: ct);
    }
}
