using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
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
    public bool TeamRegistrationAutoApprove { get; set; }
    public int MaxTeamMembers { get; set; }
    public bool TracksEnabled { get; set; }
    public List<string> TrackNames { get; set; } = [];
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
        Options(builder => builder.RequireRateLimiting("public-read"));
    }

    public override async Task HandleAsync(GetCompetitionRequest req, CancellationToken ct)
    {
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.Id)
            .FirstOrDefaultAsync(ct);

        if (competition is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (!PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await SendAsync(new CompetitionDetailDto
        {
            Id = competition.Id,
            Title = competition.Title,
            Description = competition.Description,
            Status = competition.Status.ToString().ToLowerInvariant(),
            StartTime = competition.StartTime,
            EndTime = competition.EndTime,
            GameModeType = competition.GameModeType.ToString().ToLowerInvariant(),
            TeamRegistrationAutoApprove = competition.TeamRegistrationAutoApprove,
            MaxTeamMembers = competition.MaxTeamMembers,
            TracksEnabled = competition.TracksEnabled,
            TrackNames = Admin.GetCompetitionAdminEndpoint.ParseTracks(competition.TrackNamesJson),
        }, cancellation: ct);
    }

}
