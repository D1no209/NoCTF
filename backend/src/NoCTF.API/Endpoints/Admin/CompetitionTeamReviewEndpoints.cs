using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class CompetitionTeamLockRequest
{
    public bool IsLocked { get; set; }
}

public class GetCompetitionTeamsAdminEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest<List<TeamAdminDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/teams");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var teams = await dbContext.Teams
            .IgnoreQueryFilters()
            .Where(t => t.CompetitionId == competitionId)
            .OrderByDescending(t => t.RegisteredAt)
            .Select(t => new TeamAdminDto
            {
                Id = t.Id,
                CompetitionId = t.CompetitionId,
                Name = t.Name,
                CaptainName = dbContext.Users
                    .Where(u => u.Id == t.CaptainId)
                    .Select(u => u.UserName)
                    .FirstOrDefault() ?? string.Empty,
                MemberCount = dbContext.TeamMembers.Count(tm => tm.TeamId == t.Id),
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

public class ApproveCompetitionTeamEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    ICtfScoreRebuilder ctfScoreRebuilder,
    IHubNotifierService hubNotifier)
    : EndpointWithoutRequest<TeamAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/teams/{teamId}/approve");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await dbContext.Teams
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == teamId && t.CompetitionId == competitionId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        team.RegistrationStatus = TeamRegistrationStatus.Approved;
        team.IsLocked = true;
        team.ApprovedAt = DateTime.UtcNow;
        if (Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            team.ApprovedById = userId;

        CompetitionLogWriter.Add(
            dbContext,
            competitionId,
            "team.approved",
            $"Team {team.Name} was approved.",
            teamId: team.Id,
            userId: team.ApprovedById);
        await dbContext.SaveChangesAsync(ct);
        await TeamReviewLeaderboardRefresh.RefreshAsync(
            competitionId,
            leaderboardService,
            leaderboardCache,
            ctfScoreRebuilder,
            hubNotifier,
            ct);
        await SendAsync(await ToDto(team.Id, dbContext, ct), cancellation: ct);
    }

    internal static async Task<TeamAdminDto> ToDto(Guid teamId, ApplicationDbContext dbContext, CancellationToken ct)
        => await dbContext.Teams
            .IgnoreQueryFilters()
            .Where(t => t.Id == teamId)
            .Select(t => new TeamAdminDto
            {
                Id = t.Id,
                CompetitionId = t.CompetitionId,
                Name = t.Name,
                CaptainName = dbContext.Users.Where(u => u.Id == t.CaptainId).Select(u => u.UserName).FirstOrDefault() ?? string.Empty,
                MemberCount = dbContext.TeamMembers.Count(tm => tm.TeamId == t.Id),
                CompetitionTitle = dbContext.Competitions.IgnoreQueryFilters().Where(c => c.Id == t.CompetitionId).Select(c => c.Title).FirstOrDefault() ?? string.Empty,
                InviteToken = t.InviteToken,
                IsLocked = t.IsLocked,
                IsBanned = t.IsBanned,
                BannedReason = t.BannedReason,
                TrackName = t.TrackName,
                RegistrationStatus = t.RegistrationStatus.ToString().ToLowerInvariant(),
                RegisteredAt = t.RegisteredAt,
                ApprovedAt = t.ApprovedAt,
            })
            .FirstAsync(ct);
}

public class RejectCompetitionTeamEndpoint(
    ApplicationDbContext dbContext,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    ICtfScoreRebuilder ctfScoreRebuilder,
    IHubNotifierService hubNotifier)
    : EndpointWithoutRequest<TeamAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/teams/{teamId}/reject");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await dbContext.Teams
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == teamId && t.CompetitionId == competitionId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        team.RegistrationStatus = TeamRegistrationStatus.Rejected;
        team.IsLocked = false;
        team.ApprovedAt = null;
        team.ApprovedById = null;

        CompetitionLogWriter.Add(
            dbContext,
            competitionId,
            "team.rejected",
            $"Team {team.Name} was rejected.",
            teamId: team.Id);
        await dbContext.SaveChangesAsync(ct);
        await TeamReviewLeaderboardRefresh.RefreshAsync(
            competitionId,
            leaderboardService,
            leaderboardCache,
            ctfScoreRebuilder,
            hubNotifier,
            ct);
        await SendAsync(await ApproveCompetitionTeamEndpoint.ToDto(team.Id, dbContext, ct), cancellation: ct);
    }
}

internal static class TeamReviewLeaderboardRefresh
{
    public static async Task RefreshAsync(
        Guid competitionId,
        ILeaderboardService leaderboardService,
        IRedisLeaderboardCache leaderboardCache,
        ICtfScoreRebuilder ctfScoreRebuilder,
        IHubNotifierService hubNotifier,
        CancellationToken ct)
    {
        await ctfScoreRebuilder.RebuildCompetitionAsync(competitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, ct);
        await hubNotifier.NotifyLeaderboardSnapshotAsync(
            competitionId,
            entries.Select(e => new LeaderboardEntryPayload(
                e.Rank,
                e.TeamId,
                e.TeamName,
                e.TotalScore,
                e.SolvedCount)),
            ct);
    }
}

public class SetCompetitionTeamLockEndpoint(ApplicationDbContext dbContext, ICompetitionPermissionService permissions)
    : Endpoint<CompetitionTeamLockRequest, TeamAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Put("/api/admin/competitions/{competitionId}/teams/{teamId}/lock");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(CompetitionTeamLockRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await dbContext.Teams
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == teamId && t.CompetitionId == competitionId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        team.IsLocked = req.IsLocked;
        CompetitionLogWriter.Add(
            dbContext,
            competitionId,
            req.IsLocked ? "team.locked" : "team.unlocked",
            req.IsLocked ? $"Team {team.Name} was locked." : $"Team {team.Name} was unlocked.",
            teamId: team.Id);
        await dbContext.SaveChangesAsync(ct);
        await SendAsync(await ApproveCompetitionTeamEndpoint.ToDto(team.Id, dbContext, ct), cancellation: ct);
    }
}
