using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CompetitionSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
}

public class GetCompetitionsAdminEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<CompetitionSummaryDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions");
        Roles("Admin");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var competitions = await dbContext.Competitions
            .IgnoreQueryFilters()
            .Select(c => new CompetitionSummaryDto
            {
                Id = c.Id,
                Title = c.Title,
                Status = c.Status.ToString().ToLowerInvariant(),
                OwnerId = c.OwnerId
            })
            .ToListAsync(ct);

        await SendAsync(competitions, cancellation: ct);
    }
}
