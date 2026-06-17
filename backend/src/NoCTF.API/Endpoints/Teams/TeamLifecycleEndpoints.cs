using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Teams;

public class CreateTeamRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public class JoinTeamRequest
{
    public Guid TeamId { get; set; }
}

public class LeaveTeamRequest
{
    public Guid TeamId { get; set; }
}

public class TransferCaptainRequest
{
    public Guid TeamId { get; set; }
    public Guid NewCaptainUserId { get; set; }
}

public class RemoveTeamMemberRequest
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
}

public class TeamDto
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CaptainId { get; set; }
}

public class CreateTeamEndpoint(ApplicationDbContext db) : Endpoint<CreateTeamRequest, TeamDto>
{
    public override void Configure()
    {
        Post("/api/teams");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(CreateTeamRequest req, CancellationToken ct)
    {
        var userId = GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var alreadyInTeam = await db.TeamMembers
            .AnyAsync(tm => tm.CompetitionId == req.CompetitionId && tm.UserId == userId.Value, ct);
        if (alreadyInTeam)
        {
            await SendAsync(new TeamDto(), 409, ct);
            return;
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.CompetitionId,
            Name = req.Name.Trim(),
            AvatarUrl = req.AvatarUrl,
            CaptainId = userId.Value,
            CreatedAt = DateTime.UtcNow
        };
        db.Teams.Add(team);
        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.CompetitionId,
            TeamId = team.Id,
            UserId = userId.Value,
            Role = TeamMemberRole.Captain,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await SendAsync(ToDto(team), 201, ct);
    }

    internal static Guid? GetUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    internal static TeamDto ToDto(Team team) => new()
    {
        Id = team.Id,
        CompetitionId = team.CompetitionId,
        Name = team.Name,
        CaptainId = team.CaptainId
    };
}

public class JoinTeamEndpoint(ApplicationDbContext db) : Endpoint<JoinTeamRequest, TeamDto>
{
    public override void Configure()
    {
        Post("/api/teams/{teamId}/join");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(JoinTeamRequest req, CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == req.TeamId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var alreadyInCompetition = await db.TeamMembers
            .AnyAsync(tm => tm.CompetitionId == team.CompetitionId && tm.UserId == userId.Value, ct);
        if (alreadyInCompetition)
        {
            await SendAsync(new TeamDto(), 409, ct);
            return;
        }

        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = team.CompetitionId,
            TeamId = team.Id,
            UserId = userId.Value,
            Role = TeamMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await SendAsync(CreateTeamEndpoint.ToDto(team), cancellation: ct);
    }
}

public class LeaveTeamEndpoint(ApplicationDbContext db) : Endpoint<LeaveTeamRequest>
{
    public override void Configure()
    {
        Post("/api/teams/{teamId}/leave");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(LeaveTeamRequest req, CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == req.TeamId && tm.UserId == userId.Value, ct);
        if (member is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (member.Role == TeamMemberRole.Captain &&
            await db.TeamMembers.CountAsync(tm => tm.TeamId == req.TeamId, ct) > 1)
        {
            await SendStringAsync("captain_transfer_required", 409, cancellation: ct);
            return;
        }

        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}

public class TransferCaptainEndpoint(ApplicationDbContext db, ITeamPermissionService teamPermissionService)
    : Endpoint<TransferCaptainRequest>
{
    public override void Configure()
    {
        Post("/api/teams/{teamId}/transfer-captain");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(TransferCaptainRequest req, CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (!await teamPermissionService.IsCaptainAsync(userId.Value, req.TeamId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var currentCaptain = await db.TeamMembers.FirstAsync(tm => tm.TeamId == req.TeamId && tm.UserId == userId.Value, ct);
        var newCaptain = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == req.TeamId && tm.UserId == req.NewCaptainUserId, ct);
        if (newCaptain is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        currentCaptain.Role = TeamMemberRole.Member;
        newCaptain.Role = TeamMemberRole.Captain;
        var team = await db.Teams.IgnoreQueryFilters().FirstAsync(t => t.Id == req.TeamId, ct);
        team.CaptainId = req.NewCaptainUserId;
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}

public class RemoveTeamMemberEndpoint(ApplicationDbContext db, ITeamPermissionService teamPermissionService)
    : Endpoint<RemoveTeamMemberRequest>
{
    public override void Configure()
    {
        Delete("/api/teams/{teamId}/members/{userId}");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(RemoveTeamMemberRequest req, CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (!await teamPermissionService.IsCaptainAsync(userId.Value, req.TeamId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var member = await db.TeamMembers.FirstOrDefaultAsync(tm => tm.TeamId == req.TeamId && tm.UserId == req.UserId, ct);
        if (member is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (member.Role == TeamMemberRole.Captain)
        {
            await SendStringAsync("cannot_remove_captain", 409, cancellation: ct);
            return;
        }

        db.TeamMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        await SendNoContentAsync(ct);
    }
}
