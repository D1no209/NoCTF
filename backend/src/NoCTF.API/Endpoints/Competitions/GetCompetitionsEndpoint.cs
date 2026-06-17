using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class CompetitionListItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

/// <summary>
/// GET /api/competitions — returns all published/running competitions.
/// </summary>
public class GetCompetitionsEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<CompetitionListItemDto>>
{
    public override void Configure()
    {
        Get("/api/competitions");
        AllowAnonymous();
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var competitions = await dbContext.Competitions
            .IgnoreQueryFilters()
            .Where(c => c.Status == CompetitionStatus.Published
                     || c.Status == CompetitionStatus.Running
                     || c.Status == CompetitionStatus.Finished)
            .OrderByDescending(c => c.StartTime)
            .Select(c => new CompetitionListItemDto
            {
                Id = c.Id,
                Title = c.Title,
                Description = c.Description,
                Status = c.Status.ToString().ToLowerInvariant(),
                StartTime = c.StartTime,
                EndTime = c.EndTime
            })
            .ToListAsync(ct);

        await SendAsync(competitions, cancellation: ct);
    }
}
