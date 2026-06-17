using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetCompetitionRequest
{
    public Guid Id { get; set; }
}

public class CompetitionDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string GameModeType { get; set; } = string.Empty;
}

/// <summary>
/// GET /api/competitions/{id} — returns a single competition's details.
/// </summary>
public class GetCompetitionEndpoint(ApplicationDbContext dbContext) : Endpoint<GetCompetitionRequest, CompetitionDetailDto>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetCompetitionRequest req, CancellationToken ct)
    {
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.Id == req.Id)
            .Select(c => new CompetitionDetailDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                Status = c.Status.ToString().ToLowerInvariant(),
                StartTime = c.StartTime,
                EndTime = c.EndTime,
                GameModeType = c.GameModeType.ToString().ToLowerInvariant()
            })
            .FirstOrDefaultAsync(ct);

        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(competition, cancellation: ct);
    }
}
