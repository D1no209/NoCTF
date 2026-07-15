using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using NoCTF.API.Permissions;
using NoCTF.Infrastructure;

namespace NoCTF.API.Endpoints.Teams;

public class UpdateTeamRequest
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
}

public class UpdateTeamResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class UpdateTeamEndpoint(ApplicationDbContext dbContext, ITeamPermissionService teamPermissionService)
    : Endpoint<UpdateTeamRequest, UpdateTeamResponse>
{
    public override void Configure()
    {
        Put("/api/teams/{id}");
        Options(builder => builder.RequireRateLimiting("competition-submit"));
    }

    public override async Task HandleAsync(UpdateTeamRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        await using var transaction = await TeamLifecycleRules.BeginSerializableTransactionAsync(dbContext, ct);
        await TeamLifecycleRules.AcquireTeamLockAsync(dbContext, req.Id, ct);

        var isCaptain = await teamPermissionService.IsCaptainAsync(userId, req.Id, ct);
        if (!isCaptain)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await dbContext.Teams.IgnoreQueryFilters().FirstOrDefaultAsync(
            candidate => candidate.Id == req.Id,
            ct);
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

        if (req.Name is not null) team.Name = req.Name;
        if (req.AvatarUrl is not null) team.AvatarUrl = req.AvatarUrl;

        await dbContext.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        await SendAsync(new UpdateTeamResponse { Id = team.Id, Name = team.Name }, cancellation: ct);
    }
}
