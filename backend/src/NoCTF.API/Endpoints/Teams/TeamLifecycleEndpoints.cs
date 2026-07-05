using System.Security.Claims;
using System.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Teams;

public class CreateTeamRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? TrackName { get; set; }
}

public class JoinTeamRequest
{
    public Guid TeamId { get; set; }
}

public class JoinTeamByTokenRequest
{
    public string Token { get; set; } = string.Empty;
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
    public string InviteToken { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsBanned { get; set; }
    public string? TrackName { get; set; }
    public string RegistrationStatus { get; set; } = string.Empty;
}

public class MyTeamDto : TeamDto
{
    public string CompetitionTitle { get; set; } = string.Empty;
    public string CompetitionStatus { get; set; } = string.Empty;
    public string GameModeType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int MaxTeamMembers { get; set; }
    public int MemberCount { get; set; }
    public bool IsCaptain { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class GetMyTeamsEndpoint(ApplicationDbContext db) : EndpointWithoutRequest<List<MyTeamDto>>
{
    public override void Configure()
    {
        Get("/api/teams/mine");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var teams = await db.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.UserId == userId.Value)
            .Join(db.Teams.IgnoreQueryFilters(),
                tm => tm.TeamId,
                t => t.Id,
                (tm, t) => new { Membership = tm, Team = t })
            .Join(db.Competitions.IgnoreQueryFilters(),
                x => x.Team.CompetitionId,
                c => c.Id,
                (x, c) => new MyTeamDto
                {
                    Id = x.Team.Id,
                    CompetitionId = x.Team.CompetitionId,
                    Name = x.Team.Name,
                    CaptainId = x.Team.CaptainId,
                    InviteToken = x.Team.InviteToken,
                    IsLocked = x.Team.IsLocked,
                    IsBanned = x.Team.IsBanned,
                    TrackName = x.Team.TrackName,
                    RegistrationStatus = x.Team.RegistrationStatus.ToString().ToLowerInvariant(),
                    CompetitionTitle = c.Title,
                    CompetitionStatus = c.Status.ToString().ToLowerInvariant(),
                    GameModeType = c.GameModeType.ToString(),
                    StartTime = c.StartTime,
                    EndTime = c.EndTime,
                    MaxTeamMembers = c.MaxTeamMembers,
                    MemberCount = db.TeamMembers.Count(member => member.TeamId == x.Team.Id),
                    IsCaptain = x.Team.CaptainId == userId.Value,
                    RegisteredAt = x.Team.RegisteredAt,
                    ApprovedAt = x.Team.ApprovedAt,
                })
            .OrderByDescending(t => t.RegisteredAt)
            .ToListAsync(ct);

        await SendAsync(teams, cancellation: ct);
    }
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

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CompetitionId, ct);
        if (competition is null)
        {
            await SendStringAsync("competition_not_found", 404, cancellation: ct);
            return;
        }
        if (TeamLifecycleRules.GetRegistrationBlockReason(competition) is { } registrationBlockReason)
        {
            await SendStringAsync(registrationBlockReason, 403, cancellation: ct);
            return;
        }

        var alreadyInTeam = await db.TeamMembers
            .AnyAsync(tm => tm.CompetitionId == req.CompetitionId && tm.UserId == userId.Value, ct);
        if (alreadyInTeam)
        {
            await SendAsync(new TeamDto(), 409, ct);
            return;
        }

        var trackName = req.TrackName?.Trim();
        var trackNames = Admin.GetCompetitionAdminEndpoint.ParseTracks(competition.TrackNamesJson);
        if (competition.TracksEnabled)
        {
            if (string.IsNullOrWhiteSpace(trackName) ||
                !trackNames.Any(t => string.Equals(t, trackName, StringComparison.OrdinalIgnoreCase)))
            {
                await SendStringAsync("invalid_track", 400, cancellation: ct);
                return;
            }

            trackName = trackNames.First(t => string.Equals(t, trackName, StringComparison.OrdinalIgnoreCase));
        }

        var approved = competition.TeamRegistrationAutoApprove;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            CompetitionId = req.CompetitionId,
            Name = req.Name.Trim(),
            AvatarUrl = req.AvatarUrl,
            TrackName = competition.TracksEnabled ? trackName : null,
            CaptainId = userId.Value,
            InviteToken = NewInviteToken(),
            RegistrationStatus = approved ? TeamRegistrationStatus.Approved : TeamRegistrationStatus.Pending,
            RegisteredAt = DateTime.UtcNow,
            ApprovedAt = approved ? DateTime.UtcNow : null,
            ApprovedById = approved ? userId.Value : null,
            IsLocked = approved,
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
        CompetitionLogWriter.Add(
            db,
            req.CompetitionId,
            "team.registered",
            approved ? $"Team {team.Name} registered and was auto-approved." : $"Team {team.Name} registered and is pending review.",
            teamId: team.Id,
            userId: userId.Value,
            metadata: new { team.Name, team.TrackName, approved });
        await db.SaveChangesAsync(ct);

        await SendAsync(ToDto(team), 201, ct);
    }

    internal static Guid? GetUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    internal static string NewInviteToken() => Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();

    internal static TeamDto ToDto(Team team) => new()
    {
        Id = team.Id,
        CompetitionId = team.CompetitionId,
        Name = team.Name,
        CaptainId = team.CaptainId,
        InviteToken = team.InviteToken,
        IsLocked = team.IsLocked,
        IsBanned = team.IsBanned,
        TrackName = team.TrackName,
        RegistrationStatus = team.RegistrationStatus.ToString().ToLowerInvariant()
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

        if (team.IsLocked)
        {
            await SendStringAsync("team_locked", 409, cancellation: ct);
            return;
        }

        if (team.IsBanned)
        {
            await SendStringAsync("team_banned", 403, cancellation: ct);
            return;
        }

        var joinResult = await TeamLifecycleRules.TryAddMemberAsync(db, team.Id, userId.Value, ct);
        if (!joinResult.Success)
        {
            await SendStringAsync(joinResult.Code ?? "join_failed", joinResult.StatusCode, cancellation: ct);
            return;
        }

        await SendAsync(CreateTeamEndpoint.ToDto(team), cancellation: ct);
    }
}

public class JoinTeamByTokenEndpoint(ApplicationDbContext db) : Endpoint<JoinTeamByTokenRequest, TeamDto>
{
    public override void Configure()
    {
        Post("/api/teams/join-by-token");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(JoinTeamByTokenRequest req, CancellationToken ct)
    {
        var userId = CreateTeamEndpoint.GetUserId(User);
        if (userId is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var token = req.Token.Trim();
        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.InviteToken == token, ct);
        if (team is null)
        {
            await SendStringAsync("invalid_team_token", 404, cancellation: ct);
            return;
        }

        if (team.IsLocked)
        {
            await SendStringAsync("team_locked", 409, cancellation: ct);
            return;
        }

        if (team.IsBanned)
        {
            await SendStringAsync("team_banned", 403, cancellation: ct);
            return;
        }

        var joinResult = await TeamLifecycleRules.TryAddMemberAsync(db, team.Id, userId.Value, ct);
        if (!joinResult.Success)
        {
            await SendStringAsync(joinResult.Code ?? "join_failed", joinResult.StatusCode, cancellation: ct);
            return;
        }

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

        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == req.TeamId, ct);
        if (team?.IsLocked == true)
        {
            await SendStringAsync("team_locked", 409, cancellation: ct);
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
        if (team.IsLocked)
        {
            await SendStringAsync("team_locked", 409, cancellation: ct);
            return;
        }
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

        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == req.TeamId, ct);
        if (team?.IsLocked == true)
        {
            await SendStringAsync("team_locked", 409, cancellation: ct);
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

internal static class TeamLifecycleRules
{
    public static string? GetRegistrationBlockReason(Competition competition)
    {
        var now = DateTime.UtcNow;
        if (competition.Status == CompetitionStatus.Draft)
            return "competition_not_open";
        if (competition.Status == CompetitionStatus.Paused)
            return "competition_paused";
        if (competition.Status == CompetitionStatus.Finished || now > competition.EndTime)
            return "competition_ended";

        return null;
    }

    public static async Task<(bool Success, string? Code, int StatusCode)> TryAddMemberAsync(
        ApplicationDbContext db,
        Guid teamId,
        Guid userId,
        CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

        if (transaction is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockKey(teamId)})",
                ct);

        var team = await db.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == teamId, ct);
        if (team is null)
            return (false, "team_not_found", 404);
        if (team.IsLocked)
            return (false, "team_locked", 409);
        if (team.IsBanned)
            return (false, "team_banned", 403);

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == team.CompetitionId, ct);
        if (competition is null)
            return (false, "competition_not_found", 404);
        if (GetRegistrationBlockReason(competition) is { } registrationBlockReason)
            return (false, registrationBlockReason, 403);

        var alreadyInCompetition = await db.TeamMembers
            .AnyAsync(tm => tm.CompetitionId == team.CompetitionId && tm.UserId == userId, ct);
        if (alreadyInCompetition)
            return (false, "already_registered", 409);

        var memberCount = await db.TeamMembers.CountAsync(tm => tm.TeamId == team.Id, ct);
        if (competition.MaxTeamMembers > 0 && memberCount >= competition.MaxTeamMembers)
            return (false, "team_full", 409);

        db.TeamMembers.Add(new TeamMember
        {
            Id = Guid.NewGuid(),
            CompetitionId = team.CompetitionId,
            TeamId = team.Id,
            UserId = userId,
            Role = TeamMemberRole.Member,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        return (true, null, 200);
    }

    private static long AdvisoryLockKey(Guid id)
        => BitConverter.ToInt64(id.ToByteArray(), 0);
}
