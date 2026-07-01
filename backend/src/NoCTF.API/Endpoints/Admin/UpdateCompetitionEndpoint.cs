using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class UpdateCompetitionAdminRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GameModeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public PointsConfigDto? DefaultPointsConfig { get; set; }
    public double DifficultyCoefficient { get; set; } = 1.0;
}

public class UpdateCompetitionEndpoint(ApplicationDbContext dbContext) : Endpoint<UpdateCompetitionAdminRequest>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/competitions/{id}");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(UpdateCompetitionAdminRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var competition = await dbContext.Competitions.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        competition.Title = req.Title;
        competition.Description = req.Description;
        competition.StartTime = req.StartTime;
        competition.EndTime = req.EndTime;
        competition.DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? 1.0 : req.DifficultyCoefficient;

        if (req.DefaultPointsConfig is not null)
        {
            competition.DefaultInitialPoints = req.DefaultPointsConfig.InitialPoints;
            competition.DefaultMinimumPoints = req.DefaultPointsConfig.MinimumPoints;
            competition.DefaultDecayFactor = req.DefaultPointsConfig.DecayFactor;
            competition.DefaultDecayFunction = string.IsNullOrWhiteSpace(req.DefaultPointsConfig.DecayFunction)
                ? "quadratic"
                : req.DefaultPointsConfig.DecayFunction;
        }

        if (Enum.TryParse<GameModeType>(req.GameModeType, ignoreCase: true, out var mode))
        {
            competition.GameModeType = mode;
            competition.ModeKey = CompetitionModeDefaults.GetModeKey(mode);
            competition.ScoringProfileJson = ScoringJson.Serialize(CompetitionModeDefaults.GetScoringProfile(mode));
        }

        if (Enum.TryParse<CompetitionStatus>(req.Status, ignoreCase: true, out var status))
            competition.Status = status;

        await dbContext.SaveChangesAsync(ct);
        await SendOkAsync(ct);
    }
}
