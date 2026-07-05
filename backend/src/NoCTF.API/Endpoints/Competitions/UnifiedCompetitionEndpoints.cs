using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API;
using NoCTF.Application.CompetitionModes;
using NoCTF.Application.Leaderboard;
using NoCTF.Application.Scoring;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Competitions;

public class GetCompetitionCapabilitiesRequest
{
    public Guid Id { get; set; }
}

public class CompetitionCapabilitiesResponse
{
    public Guid CompetitionId { get; set; }
    public string ModeKey { get; set; } = string.Empty;
    public List<string> Actions { get; set; } = [];
    public List<string> Views { get; set; } = [];
    public List<string> Jobs { get; set; } = [];
    public List<string> ScoringProfile { get; set; } = [];
}

public class GetCompetitionCapabilitiesEndpoint(
    ApplicationDbContext db,
    ICompetitionModeRegistry modeRegistry,
    ICompetitionScoringProfileResolver scoringProfileResolver)
    : Endpoint<GetCompetitionCapabilitiesRequest, CompetitionCapabilitiesResponse>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/capabilities");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetCompetitionCapabilitiesRequest req, CancellationToken ct)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

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

        var modeKey = ResolveModeKey(competition.ModeKey, competition.GameModeType.ToString());
        var provider = modeRegistry.GetRequiredProvider(modeKey);
        var capabilities = provider.GetCapabilities();
        var scoringProfile = await scoringProfileResolver.ResolveAsync(req.Id, ct);

        await SendAsync(new CompetitionCapabilitiesResponse
        {
            CompetitionId = req.Id,
            ModeKey = modeKey,
            Actions = capabilities.Actions.ToList(),
            Views = capabilities.Views.ToList(),
            Jobs = capabilities.Jobs.ToList(),
            ScoringProfile = scoringProfile.ToList()
        }, cancellation: ct);
    }

    private static string ResolveModeKey(string? modeKey, string fallback)
        => string.IsNullOrWhiteSpace(modeKey) ? fallback.ToLowerInvariant() : modeKey.Trim().ToLowerInvariant();
}

public class CompetitionActionResponse
{
    public bool Success { get; set; }
    public string Code { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class PostCompetitionActionEndpoint(ApplicationDbContext db, ICompetitionModeRegistry modeRegistry)
    : EndpointWithoutRequest<CompetitionActionResponse>, IAuditableEndpoint
{
    private const int MaxActionPayloadBytes = 64 * 1024;

    public override void Configure()
    {
        Post("/api/competitions/{id}/actions/{actionKey}");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");
        var actionKey = Route<string>("actionKey") ?? string.Empty;
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);

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

        if (PublicCompetitionGuard.ResolvePlayBlockReason(competition, DateTime.UtcNow) is { } blockReason)
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = blockReason
            }, 403, ct);
            return;
        }

        var team = await db.TeamMembers
            .IgnoreQueryFilters()
            .Where(tm => tm.CompetitionId == competitionId && tm.UserId == userId)
            .Join(db.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == competitionId),
                tm => tm.TeamId,
                t => t.Id,
                (_, t) => new { TeamId = t.Id, t.RegistrationStatus, t.IsBanned })
            .FirstOrDefaultAsync(ct);

        if (team is { RegistrationStatus: not TeamRegistrationStatus.Approved })
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = "team_not_approved"
            }, 403, ct);
            return;
        }

        if (team is { IsBanned: true })
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = "team_banned"
            }, 403, ct);
            return;
        }

        if (HttpContext.Request.ContentLength is > MaxActionPayloadBytes)
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = "payload_too_large"
            }, 413, ct);
            return;
        }

        string payloadJson;
        try
        {
            using var document = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: ct);
            payloadJson = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.GetRawText()
                : "{}";
        }
        catch (JsonException)
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = "invalid_json"
            }, 400, ct);
            return;
        }
        var modeKey = string.IsNullOrWhiteSpace(competition.ModeKey)
            ? competition.GameModeType.ToString().ToLowerInvariant()
            : competition.ModeKey.Trim().ToLowerInvariant();

        var provider = modeRegistry.GetRequiredProvider(modeKey);
        if (!provider.CanHandleAction(actionKey))
        {
            await SendAsync(new CompetitionActionResponse
            {
                Success = false,
                Code = "unsupported_action"
            }, 404, ct);
            return;
        }

        var result = await provider.HandleActionAsync(new CompetitionActionContext(
            competitionId,
            team?.TeamId,
            userId,
            actionKey,
            payloadJson,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"), ct);

        await SendAsync(new CompetitionActionResponse
        {
            Success = result.Success,
            Code = result.Code,
            Data = result.Data
        }, result.Success ? 200 : 400, ct);
    }
}

public class GetCompetitionScoreboardRequest
{
    public Guid Id { get; set; }
}

public class GetCompetitionViewEndpoint(ApplicationDbContext db, ICompetitionModeRegistry modeRegistry)
    : EndpointWithoutRequest<CompetitionViewResult>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/views/{viewKey}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("id");
        var viewKey = Route<string>("viewKey") ?? string.Empty;
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == competitionId, ct);

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

        if (DateTime.UtcNow < competition.StartTime && competition.Status != CompetitionStatus.Finished)
        {
            await SendAsync(new CompetitionViewResult(viewKey, new { code = "competition_not_started" }), 403, ct);
            return;
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid? userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : null;
        Guid? teamId = null;
        if (userId.HasValue)
        {
            teamId = await db.TeamMembers
                .IgnoreQueryFilters()
                .Where(tm => tm.CompetitionId == competitionId && tm.UserId == userId.Value)
                .Join(db.Teams.IgnoreQueryFilters().Where(t =>
                        t.CompetitionId == competitionId &&
                        t.RegistrationStatus == TeamRegistrationStatus.Approved &&
                        !t.IsBanned),
                    tm => tm.TeamId,
                    t => t.Id,
                    (_, t) => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);
        }

        var modeKey = string.IsNullOrWhiteSpace(competition.ModeKey)
            ? competition.GameModeType.ToString().ToLowerInvariant()
            : competition.ModeKey.Trim().ToLowerInvariant();

        var provider = modeRegistry.GetRequiredProvider(modeKey);
        if (!provider.CanProvideView(viewKey))
        {
            await SendAsync(new CompetitionViewResult(viewKey, new { code = "unsupported_view" }), 404, ct);
            return;
        }

        var view = await provider.GetViewAsync(new CompetitionViewContext(
            competitionId,
            teamId,
            userId,
            viewKey), ct);

        await SendAsync(view, cancellation: ct);
    }
}

public class GetCompetitionScoreboardEndpoint(ILeaderboardProjectionBuilder projectionBuilder, ApplicationDbContext db)
    : Endpoint<GetCompetitionScoreboardRequest, IReadOnlyList<ScoreboardRow>>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/scoreboard");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetCompetitionScoreboardRequest req, CancellationToken ct)
    {
        var competition = await db.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == req.Id)
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

        var rows = await projectionBuilder.BuildAsync(req.Id, ct);
        await SendAsync(rows, cancellation: ct);
    }
}
