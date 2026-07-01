using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CompetitionSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GameModeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public Guid OwnerId { get; set; }
    public PointsConfigDto DefaultPointsConfig { get; set; } = new();
    public double DifficultyCoefficient { get; set; } = 1.0;
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
                Description = c.Description,
                GameModeType = c.GameModeType.ToString(),
                Status = c.Status.ToString().ToLowerInvariant(),
                StartTime = c.StartTime,
                EndTime = c.EndTime,
                OwnerId = c.OwnerId,
                DefaultPointsConfig = new PointsConfigDto
                {
                    InitialPoints = c.DefaultInitialPoints,
                    MinimumPoints = c.DefaultMinimumPoints,
                    DecayFactor = c.DefaultDecayFactor,
                    DecayFunction = c.DefaultDecayFunction,
                },
                DifficultyCoefficient = c.DifficultyCoefficient,
            })
            .ToListAsync(ct);

        await SendAsync(competitions, cancellation: ct);
    }
}

public class GetCompetitionAdminEndpoint(ApplicationDbContext dbContext) : EndpointWithoutRequest<CompetitionSummaryDto>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new CompetitionSummaryDto
        {
            Id = competition.Id,
            Title = competition.Title,
            Description = competition.Description,
            GameModeType = competition.GameModeType.ToString(),
            Status = competition.Status.ToString().ToLowerInvariant(),
            StartTime = competition.StartTime,
            EndTime = competition.EndTime,
            OwnerId = competition.OwnerId,
            DefaultPointsConfig = new PointsConfigDto
            {
                InitialPoints = competition.DefaultInitialPoints,
                MinimumPoints = competition.DefaultMinimumPoints,
                DecayFactor = competition.DefaultDecayFactor,
                DecayFunction = competition.DefaultDecayFunction,
            },
            DifficultyCoefficient = competition.DifficultyCoefficient,
        }, cancellation: ct);
    }
}
