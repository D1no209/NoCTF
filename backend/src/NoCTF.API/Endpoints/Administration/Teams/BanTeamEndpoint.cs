using FastEndpoints;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class BanTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BanTeamEndpoint(ModerateTeam moderate, IUserContext userContext)
    : Endpoint<BanTeamRequest>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/ban");
        AuthSchemes("Bearer");
        Roles("Administrator", "Organizer");
    }

    public override async Task HandleAsync(BanTeamRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        var result = await moderate.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            userContext.UserId,
            true,
            request.Reason,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            await HttpContext.Response.SendStatusCodeAsync(StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }
        await HttpContext.Response.SendNoContentAsync(cancellationToken);
    }
}
