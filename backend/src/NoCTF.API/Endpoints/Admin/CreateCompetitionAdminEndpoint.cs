using FastEndpoints;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class CreateCompetitionAdminRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GameModeType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public PointsConfigDto? DefaultPointsConfig { get; set; }
    public double DifficultyCoefficient { get; set; } = 1.0;
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public bool TracksEnabled { get; set; }
    public List<string> TrackNames { get; set; } = [];
}

public class CreateCompetitionAdminEndpoint(ApplicationDbContext dbContext)
    : Endpoint<CreateCompetitionAdminRequest, CompetitionSummaryDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CreateCompetitionAdminRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        Guid.TryParse(userIdClaim, out var userId);

        var mode = Enum.TryParse<GameModeType>(req.GameModeType, ignoreCase: true, out var parsedMode)
            ? parsedMode
            : GameModeType.Ctf;

        var status = Enum.TryParse<CompetitionStatus>(req.Status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : CompetitionStatus.Draft;

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Title = req.Title,
            Description = req.Description,
            GameModeType = mode,
            ModeKey = CompetitionModeDefaults.GetModeKey(mode),
            ScoringProfileJson = ScoringJson.Serialize(CompetitionModeDefaults.GetScoringProfile(mode)),
            OwnerId = userId,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Status = status,
            DefaultInitialPoints = req.DefaultPointsConfig?.InitialPoints ?? 500,
            DefaultMinimumPoints = req.DefaultPointsConfig?.MinimumPoints ?? 100,
            DefaultDecayFactor = req.DefaultPointsConfig?.DecayFactor ?? 450,
            DefaultDecayFunction = req.DefaultPointsConfig?.DecayFunction ?? "quadratic",
            DifficultyCoefficient = req.DifficultyCoefficient <= 0 ? 1.0 : req.DifficultyCoefficient,
            TeamRegistrationAutoApprove = req.TeamRegistrationAutoApprove,
            MaxTeamMembers = req.MaxTeamMembers <= 0 ? 5 : req.MaxTeamMembers,
            TracksEnabled = req.TracksEnabled,
            TrackNamesJson = System.Text.Json.JsonSerializer.Serialize(NormalizeTracks(req.TrackNames)),
        };
        competition.CompetitionId = competition.Id;

        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(ct);

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
            TeamRegistrationAutoApprove = competition.TeamRegistrationAutoApprove,
            MaxTeamMembers = competition.MaxTeamMembers,
            TracksEnabled = competition.TracksEnabled,
            TrackNames = NormalizeTracks(req.TrackNames),
        }, 201, ct);
    }

    internal static List<string> NormalizeTracks(IEnumerable<string> tracks)
        => tracks
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
