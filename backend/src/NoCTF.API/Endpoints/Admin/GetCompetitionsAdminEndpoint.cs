using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;
using System.Text.Json;

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
    public double FirstBloodBonusPercent { get; set; }
    public double SecondBloodBonusPercent { get; set; }
    public double ThirdBloodBonusPercent { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public bool TracksEnabled { get; set; }
    public List<string> TrackNames { get; set; } = [];
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
            .ToListAsync(ct);

        await SendAsync(competitions.Select(GetCompetitionAdminEndpoint.ToDto).ToList(), cancellation: ct);
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

        await SendAsync(ToDto(competition), cancellation: ct);
    }

    internal static CompetitionSummaryDto ToDto(Competition competition) => new()
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
        FirstBloodBonusPercent = competition.FirstBloodBonusPercent,
        SecondBloodBonusPercent = competition.SecondBloodBonusPercent,
        ThirdBloodBonusPercent = competition.ThirdBloodBonusPercent,
        TeamRegistrationAutoApprove = competition.TeamRegistrationAutoApprove,
        MaxTeamMembers = competition.MaxTeamMembers,
        TracksEnabled = competition.TracksEnabled,
        TrackNames = ParseTracks(competition.TrackNamesJson),
    };

    internal static List<string> ParseTracks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; }
        catch (JsonException) { return []; }
    }
}
