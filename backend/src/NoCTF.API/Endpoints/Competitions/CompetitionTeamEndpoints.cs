using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
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
