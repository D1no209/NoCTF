using System.Security.Claims;
using System.Text.Json;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.CompetitionModes;
using NoCTF.Core;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;

namespace NoCTF.API.Endpoints.Competitions;

public class PenetrationChallengeRequest
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
}

public class PenetrationFlagSubmitRequest : PenetrationChallengeRequest
{
    public string Flag { get; set; } = string.Empty;
}

public class PenetrationFlagSubmitResponse
{
    public bool Correct { get; set; }
    public bool AlreadySolved { get; set; }
    public string Result { get; set; } = string.Empty;
    public object? Data { get; set; }
}

internal static class PenetrationEndpointRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<PlayerChallengeContext> LoadPlayerContextAsync(
        ApplicationDbContext db,
        Guid competitionId,
        Guid challengeId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
            return PlayerChallengeContext.Fail("unauthorized", 401);

        var team = await db.TeamMembers
            .AsNoTracking()
            .Where(tm => tm.CompetitionId == competitionId && tm.UserId == userId)
            .Join(db.Teams.IgnoreQueryFilters().Where(t => t.CompetitionId == competitionId),
                tm => tm.TeamId,
                t => t.Id,
                (_, t) => t)
            .FirstOrDefaultAsync(ct);
        if (team is null) return PlayerChallengeContext.Fail("no_team", 400);
        if (team.RegistrationStatus != TeamRegistrationStatus.Approved) return PlayerChallengeContext.Fail("team_not_approved", 403);
        if (team.IsBanned) return PlayerChallengeContext.Fail("team_banned", 403);

        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.CompetitionId == competitionId &&
                c.Id == challengeId &&
                !c.IsDeleting,
                ct);
        if (challenge is null) return PlayerChallengeContext.Fail("challenge_not_found", 404);

        return new PlayerChallengeContext(userId, team, challenge, null);
    }

    public static ChallengeFeatureContext ToFeatureContext(
        PlayerChallengeContext context,
        Guid competitionId,
        Guid challengeId,
        string featureKey,
        HttpContext httpContext,
        object? payload = null)
        => new(
            CompetitionId: competitionId,
            ChallengeId: challengeId,
            TeamId: context.Team!.Id,
            UserId: context.UserId,
            TypeId: context.Challenge!.TypeId,
            FeatureKey: featureKey,
            PayloadJson: payload is null ? "{}" : JsonSerializer.Serialize(payload, JsonOptions),
            IpAddress: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    public static int StatusFromException(InvalidOperationException ex)
        => ex.Message switch
        {
            "challenge_not_found" or "instance_not_found" => 404,
            "instance_cooldown" or "flag_rate_limited" => 429,
            "instance_busy" or "reset_limit_exceeded" or "active_instances_exist" => 409,
            "competition_not_started" or "competition_ended" or "competition_paused" => 403,
            _ when IsPublicErrorCode(ex.Message) => 400,
            _ => 500
        };

    public static string ErrorCodeFromException(InvalidOperationException ex)
        => IsPublicErrorCode(ex.Message) ? ex.Message : "instance_operation_failed";

    private static bool IsPublicErrorCode(string message)
        => message.Length is > 0 and <= 64 &&
           message.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
}

internal readonly record struct PlayerChallengeError(string Message, int StatusCode);

internal sealed record PlayerChallengeContext(
    Guid UserId,
    Team? Team,
    Challenge? Challenge,
    PlayerChallengeError? Error)
{
    public static PlayerChallengeContext Fail(string message, int statusCode)
        => new(Guid.Empty, null, null, new PlayerChallengeError(message, statusCode));
}

public class GetPenetrationChallengeEndpoint(
    ApplicationDbContext db,
    IChallengeFeatureRegistry registry)
    : Endpoint<PenetrationChallengeRequest>
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/challenges/{challengeId}/penetration");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.detail", ct);

    protected async Task HandleFeatureAsync(PenetrationChallengeRequest req, string featureKey, CancellationToken ct)
    {
        var context = await PenetrationEndpointRuntime.LoadPlayerContextAsync(db, req.Id, req.ChallengeId, User, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var provider = registry.FindProvider(context.Challenge!.TypeId);
        if (provider is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        try
        {
            var result = await provider.HandleAsync(
                PenetrationEndpointRuntime.ToFeatureContext(context, req.Id, req.ChallengeId, featureKey, HttpContext),
                ct);
            await SendAsync(result.Data, result.StatusCode, ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendStringAsync(
                PenetrationEndpointRuntime.ErrorCodeFromException(ex),
                PenetrationEndpointRuntime.StatusFromException(ex),
                cancellation: ct);
        }
    }
}

public class GetPenetrationInstanceEndpoint(ApplicationDbContext db, IChallengeFeatureRegistry registry)
    : GetPenetrationChallengeEndpoint(db, registry)
{
    public override void Configure()
    {
        Get("/api/competitions/{id}/challenges/{challengeId}/penetration/instance");
        Claims(ClaimTypes.NameIdentifier);
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.instance.get", ct);
}

public class StartPenetrationInstanceEndpoint(ApplicationDbContext db, IChallengeFeatureRegistry registry)
    : GetPenetrationChallengeEndpoint(db, registry)
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/penetration/instance/start");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.instance.start", ct);
}

public class StopPenetrationInstanceEndpoint(ApplicationDbContext db, IChallengeFeatureRegistry registry)
    : GetPenetrationChallengeEndpoint(db, registry)
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/penetration/instance/stop");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.instance.stop", ct);
}

public class ResetPenetrationInstanceEndpoint(ApplicationDbContext db, IChallengeFeatureRegistry registry)
    : GetPenetrationChallengeEndpoint(db, registry)
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/penetration/instance/reset");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.instance.reset", ct);
}

public class DestroyPenetrationInstanceEndpoint(ApplicationDbContext db, IChallengeFeatureRegistry registry)
    : GetPenetrationChallengeEndpoint(db, registry)
{
    public override void Configure()
    {
        Delete("/api/competitions/{id}/challenges/{challengeId}/penetration/instance");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(PenetrationChallengeRequest req, CancellationToken ct)
        => await HandleFeatureAsync(req, "penetration.player.instance.destroy", ct);
}

public class SubmitPenetrationFlagEndpoint(
    ApplicationDbContext db,
    IChallengeSubmissionHandlerRegistry registry,
    IConfiguration configuration)
    : Endpoint<PenetrationFlagSubmitRequest, PenetrationFlagSubmitResponse>
{
    public override void Configure()
    {
        Post("/api/competitions/{id}/challenges/{challengeId}/penetration/flags/submit");
        Claims(ClaimTypes.NameIdentifier);
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(PenetrationFlagSubmitRequest req, CancellationToken ct)
    {
        var submittedFlag = req.Flag?.Trim() ?? string.Empty;
        var maxFlagLength = configuration.GetValue("Submissions:MaxFlagLength", 1024);
        if (submittedFlag.Length == 0 || submittedFlag.Length > maxFlagLength)
        {
            await SendAsync(new PenetrationFlagSubmitResponse { Result = "invalid_flag_format" }, 400, ct);
            return;
        }
        req.Flag = submittedFlag;

        var context = await PenetrationEndpointRuntime.LoadPlayerContextAsync(db, req.Id, req.ChallengeId, User, ct);
        if (context.Error is not null)
        {
            await SendStringAsync(context.Error.Value.Message, context.Error.Value.StatusCode, cancellation: ct);
            return;
        }

        var handler = registry.FindHandler(context.Challenge!.TypeId);
        if (handler is null)
        {
            await SendStringAsync("challenge_type_plugin_unavailable", 503, cancellation: ct);
            return;
        }

        var submissionContext = new SubmissionContext(
            CompetitionId: req.Id,
            TeamId: context.Team!.Id,
            ChallengeId: req.ChallengeId,
            UserId: context.UserId,
            FlagContent: req.Flag,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        var result = await handler.ProcessSubmissionAsync(submissionContext, context.Challenge, ct);

        await SendAsync(result.Result switch
        {
            SubmissionResult.Accepted => new PenetrationFlagSubmitResponse { Correct = true, Result = "accepted", Data = result.Data },
            SubmissionResult.AlreadySolved => new PenetrationFlagSubmitResponse { Correct = true, AlreadySolved = true, Result = "already_solved", Data = result.Data },
            SubmissionResult.InstanceRequired => new PenetrationFlagSubmitResponse { Result = "instance_required" },
            SubmissionResult.InstanceExpired => new PenetrationFlagSubmitResponse { Result = "instance_expired" },
            SubmissionResult.FlagRateLimited => new PenetrationFlagSubmitResponse { Result = "flag_rate_limited" },
            SubmissionResult.CompetitionNotStarted => new PenetrationFlagSubmitResponse { Result = "competition_not_started" },
            SubmissionResult.CompetitionEnded => new PenetrationFlagSubmitResponse { Result = "competition_ended" },
            SubmissionResult.CompetitionPaused => new PenetrationFlagSubmitResponse { Result = "competition_paused" },
            _ => new PenetrationFlagSubmitResponse { Result = "wrong_flag" }
        }, cancellation: ct);
    }
}
