using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class MyCompetitionTeamDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CaptainId { get; set; }
    public bool IsCaptain { get; set; }
    public string InviteToken { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public bool IsLocked { get; set; }
    public bool IsBanned { get; set; }
    public string? BannedReason { get; set; }
    public string? TrackName { get; set; }
    public string RegistrationStatus { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class CompetitionTeamListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TrackName { get; set; }
    public int MemberCount { get; set; }
}

public class GetCompetitionTeamsEndpoint(ApplicationDbContext dbContext)
    : EndpointWithoutRequest<List<CompetitionTeamListDto>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/teams");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Status, c.StartTime })
            .FirstOrDefaultAsync(ct);
        if (competition is null || !PublicCompetitionGuard.IsPublic(competition.Status))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync([], cancellation: ct);
            return;
        }

        var teams = await dbContext.Teams
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t =>
                t.CompetitionId == competitionId &&
                t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                !t.IsBanned)
            .OrderBy(t => t.Name)
            .Select(t => new CompetitionTeamListDto
            {
                Id = t.Id,
                Name = t.Name,
                TrackName = t.TrackName,
                MemberCount = dbContext.TeamMembers.Count(m => m.CompetitionId == competitionId && m.TeamId == t.Id)
            })
            .ToListAsync(ct);

        await SendAsync(teams, cancellation: ct);
    }
}

public class GetMyCompetitionTeamsEndpoint(ApplicationDbContext dbContext)
    : EndpointWithoutRequest<List<MyCompetitionTeamDto>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/teams/mine");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var competitionId = Route<Guid>("id");
        var teams = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.CompetitionId == competitionId && tm.UserId == userId)
            .Join(dbContext.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == competitionId),
                tm => tm.TeamId,
                t => t.Id,
                (tm, t) => new MyCompetitionTeamDto
                {
                    Id = t.Id,
                    CompetitionId = t.CompetitionId,
                    Name = t.Name,
                    CaptainId = t.CaptainId,
                    IsCaptain = t.CaptainId == userId,
                    InviteToken = t.InviteToken,
                    MemberCount = dbContext.TeamMembers.Count(m => m.TeamId == t.Id),
                    IsLocked = t.IsLocked,
                    IsBanned = t.IsBanned,
                    BannedReason = t.BannedReason,
                    TrackName = t.TrackName,
                    RegistrationStatus = t.RegistrationStatus.ToString().ToLowerInvariant(),
                    RegisteredAt = t.RegisteredAt,
                    ApprovedAt = t.ApprovedAt,
                })
            .ToListAsync(ct);

        await SendAsync(teams, cancellation: ct);
    }
}
