using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.API.Permissions;
using NoCTF.Application;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Admin;

public class CompetitionLogDto
{
    public Guid Id { get; set; }
    public string Level { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public string? TeamName { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string? ChallengeTitle { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class CheatIncidentDto
{
    public Guid Id { get; set; }
    public Guid SuspectTeamId { get; set; }
    public string SuspectTeamName { get; set; } = string.Empty;
    public Guid? VictimTeamId { get; set; }
    public string? VictimTeamName { get; set; }
    public Guid ChallengeId { get; set; }
    public string ChallengeTitle { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string SubmittedFlag { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TeamBanRequest
{
    public string? Reason { get; set; }
}

public class GetCompetitionLogsAdminEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest<List<CompetitionLogDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/logs");
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

        var logs = await db.CompetitionLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.CompetitionId == competitionId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(300)
            .Select(l => new CompetitionLogDto
            {
                Id = l.Id,
                Level = l.Level,
                EventType = l.EventType,
                Message = l.Message,
                TeamId = l.TeamId,
                TeamName = l.TeamId == null ? null : db.Teams.IgnoreQueryFilters().Where(t => t.Id == l.TeamId).Select(t => t.Name).FirstOrDefault(),
                UserId = l.UserId,
                ChallengeId = l.ChallengeId,
                ChallengeTitle = l.ChallengeId == null ? null : db.Challenges.IgnoreQueryFilters().Where(c => c.Id == l.ChallengeId).Select(c => c.Title).FirstOrDefault(),
                MetadataJson = l.MetadataJson,
                CreatedAt = l.CreatedAt,
            })
            .ToListAsync(ct);

        await SendAsync(logs, cancellation: ct);
    }
}

public class GetCompetitionCheatIncidentsEndpoint(ApplicationDbContext db, ICompetitionPermissionService permissions)
    : EndpointWithoutRequest<List<CheatIncidentDto>>
{
    public override void Configure()
    {
        Get("/api/admin/competitions/{competitionId}/cheat-incidents");
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

        var incidents = await db.CheatIncidents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.CompetitionId == competitionId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(300)
            .Select(i => new CheatIncidentDto
            {
                Id = i.Id,
                SuspectTeamId = i.SuspectTeamId,
                SuspectTeamName = db.Teams.IgnoreQueryFilters().Where(t => t.Id == i.SuspectTeamId).Select(t => t.Name).FirstOrDefault() ?? string.Empty,
                VictimTeamId = i.VictimTeamId,
                VictimTeamName = i.VictimTeamId == null ? null : db.Teams.IgnoreQueryFilters().Where(t => t.Id == i.VictimTeamId).Select(t => t.Name).FirstOrDefault(),
                ChallengeId = i.ChallengeId,
                ChallengeTitle = db.Challenges.IgnoreQueryFilters().Where(c => c.Id == i.ChallengeId).Select(c => c.Title).FirstOrDefault() ?? string.Empty,
                UserId = i.UserId,
                UserName = db.Users.Where(u => u.Id == i.UserId).Select(u => u.UserName).FirstOrDefault() ?? string.Empty,
                SubmittedFlag = i.SubmittedFlag,
                Reason = i.Reason,
                Resolved = i.Resolved,
                CreatedAt = i.CreatedAt,
            })
            .ToListAsync(ct);

        await SendAsync(incidents, cancellation: ct);
    }
}

public class BanCompetitionTeamEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    IHubNotifierService hubNotifier,
    ICtfScoreRebuilder ctfScoreRebuilder,
    IContainerManager containerManager)
    : Endpoint<TeamBanRequest, TeamAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/teams/{teamId}/ban");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(TeamBanRequest req, CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var teamId = Route<Guid>("teamId");
        if (!await AdminCompetitionAuthorization.CanManageAsync(HttpContext, permissions, competitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await db.Teams.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.Id == teamId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        team.IsBanned = true;
        team.BannedAt = DateTime.UtcNow;
        team.BannedReason = string.IsNullOrWhiteSpace(req.Reason) ? "cheat_suspected" : req.Reason.Trim();
        if (Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
            team.BannedById = userId;

        await ContainerCleanupRuntime.CleanupTeamAsync(
            db,
            containerManager,
            competitionId,
            team.Id,
            HttpContext,
            team.BannedById,
            "team_banned",
            ct);

        db.AwdFlags.RemoveRange(await db.AwdFlags
            .IgnoreQueryFilters()
            .Where(f => f.CompetitionId == competitionId && f.TeamId == team.Id)
            .ToListAsync(ct));
        var activeKohRecords = await db.KohControlRecords
            .IgnoreQueryFilters()
            .Where(r => r.CompetitionId == competitionId && r.TeamId == team.Id && r.EndTime == null)
            .ToListAsync(ct);
        foreach (var record in activeKohRecords)
            record.EndTime = DateTime.UtcNow;

        CompetitionLogWriter.Add(
            db,
            competitionId,
            "team.banned",
            $"Team {team.Name} was banned for this competition.",
            "error",
            teamId: team.Id,
            userId: team.BannedById,
            metadata: new { team.BannedReason });
        await db.SaveChangesAsync(ct);
        await ctfScoreRebuilder.RebuildCompetitionAsync(competitionId, ct);
        await RefreshLeaderboardAsync(competitionId, ct);
        await SendAsync(await ApproveCompetitionTeamEndpoint.ToDto(team.Id, db, ct), cancellation: ct);
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
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

public class UnbanCompetitionTeamEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissions,
    ILeaderboardService leaderboardService,
    IRedisLeaderboardCache leaderboardCache,
    ICtfScoreRebuilder ctfScoreRebuilder,
    IHubNotifierService hubNotifier)
    : EndpointWithoutRequest<TeamAdminDto>, IAuditableEndpoint
{
    public override void Configure()
    {
        Post("/api/admin/competitions/{competitionId}/teams/{teamId}/unban");
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

        var team = await db.Teams.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CompetitionId == competitionId && t.Id == teamId, ct);
        if (team is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        team.IsBanned = false;
        team.BannedAt = null;
        team.BannedById = null;
        team.BannedReason = null;

        CompetitionLogWriter.Add(
            db,
            competitionId,
            "team.unbanned",
            $"Team {team.Name} was unbanned for this competition.",
            teamId: team.Id);
        await db.SaveChangesAsync(ct);
        await ctfScoreRebuilder.RebuildCompetitionAsync(competitionId, ct);
        await RefreshLeaderboardAsync(competitionId, ct);
        await SendAsync(await ApproveCompetitionTeamEndpoint.ToDto(team.Id, db, ct), cancellation: ct);
    }

    private async Task RefreshLeaderboardAsync(Guid competitionId, CancellationToken ct)
    {
        var cacheVersion = await leaderboardCache.ReserveUpdateVersionAsync(competitionId, ct);
        var entries = await leaderboardService.CalculateLeaderboardAsync(competitionId, ct);
        await leaderboardCache.UpdateAsync(competitionId, entries, cacheVersion, ct);
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
