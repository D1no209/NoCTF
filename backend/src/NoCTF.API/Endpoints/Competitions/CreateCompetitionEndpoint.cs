using FastEndpoints;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

public class CreateCompetitionRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameModeType GameModeType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class CreateCompetitionResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CreateCompetitionEndpoint(ApplicationDbContext dbContext) : Endpoint<CreateCompetitionRequest, CreateCompetitionResponse>
{
    public override void Configure()
    {
        Post("/api/competitions");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CreateCompetitionRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdClaim, out var userId);

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            CompetitionId = Guid.NewGuid(),
            Title = req.Title,
            Description = req.Description,
            GameModeType = req.GameModeType,
            ModeKey = CompetitionModeDefaults.GetModeKey(req.GameModeType),
            ScoringProfileJson = ScoringJson.Serialize(CompetitionModeDefaults.GetScoringProfile(req.GameModeType)),
            OwnerId = userId,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Status = CompetitionStatus.Draft
        };

        // Set CompetitionId == Id (tenant is itself)
        competition.CompetitionId = competition.Id;

        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(ct);

        await SendAsync(new CreateCompetitionResponse
        {
            Id = competition.Id,
            Title = competition.Title,
            Status = competition.Status.ToString().ToLowerInvariant()
        }, 201, ct);
    }
}
