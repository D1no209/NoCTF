using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class TeamAdminDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CaptainName { get; set; } = string.Empty;
    public int MemberCount { get; set; }
    public string CompetitionTitle { get; set; } = string.Empty;
    public string InviteToken { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsBanned { get; set; }
    public string? BannedReason { get; set; }
    public string? TrackName { get; set; }
    public string RegistrationStatus { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class GetTeamsEndpoint(ApplicationDbContext dbContext) : Endpoint<EmptyRequest, List<TeamAdminDto>>
{
    public override void Configure()
    {
        Get("/api/admin/teams");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        var teams = await dbContext.Teams
            .IgnoreQueryFilters()
            .Select(t => new TeamAdminDto
            {
                Id = t.Id,
                CompetitionId = t.CompetitionId,
                Name = t.Name,
                CaptainName = dbContext.Users
                    .Where(u => u.Id == t.CaptainId)
                    .Select(u => u.UserName)
                    .FirstOrDefault() ?? string.Empty,
                MemberCount = dbContext.TeamMembers
                    .Count(tm => tm.TeamId == t.Id),
                CompetitionTitle = dbContext.Competitions
                    .IgnoreQueryFilters()
                    .Where(c => c.Id == t.CompetitionId)
                    .Select(c => c.Title)
                    .FirstOrDefault() ?? string.Empty,
                InviteToken = t.InviteToken,
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
