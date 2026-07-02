using FastEndpoints;
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
    }

    public override async Task HandleAsync(UpdateTeamRequest req, CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        var isCaptain = await teamPermissionService.IsCaptainAsync(userId, req.Id, ct);
        if (!isCaptain)
        {
            await SendForbiddenAsync(ct);
            return;
        }

        var team = await dbContext.Teams.FindAsync(new object[] { req.Id }, ct);
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

        await SendAsync(new UpdateTeamResponse { Id = team.Id, Name = team.Name }, cancellation: ct);
    }
}
