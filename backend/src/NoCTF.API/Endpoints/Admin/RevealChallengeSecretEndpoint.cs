using System.Security.Claims;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Admin;

public class RevealChallengeSecretRequest
{
    public Guid Id { get; set; }
}

public class RevealChallengeSecretResponse
{
    public Guid ChallengeId { get; set; }
    public string? FlagSecret { get; set; }
}

public class RevealChallengeSecretEndpoint(
    ApplicationDbContext db,
    ICompetitionPermissionService permissionService) : Endpoint<RevealChallengeSecretRequest, RevealChallengeSecretResponse>
{
    public override void Configure()
    {
        Post("/api/admin/challenges/{id}/reveal-secret");
        Roles("Admin", "Organizer");
    }

    public override async Task HandleAsync(RevealChallengeSecretRequest req, CancellationToken ct)
    {
        var challenge = await db.Challenges
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);

        if (challenge is null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        if (!await permissionService.CanManageCompetitionAsync(userId, challenge.CompetitionId, ct))
        {
            await SendForbiddenAsync(ct);
            return;
        }

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserName = User.Identity?.Name,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
            Action = "RevealChallengeSecret",
            EntityType = "Challenge",
            EntityId = challenge.Id.ToString(),
            EndpointPath = HttpContext.Request.Path,
            HttpMethod = HttpContext.Request.Method,
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await SendAsync(new RevealChallengeSecretResponse
        {
            ChallengeId = challenge.Id,
            FlagSecret = challenge.FlagSecret
        }, cancellation: ct);
    }
}
