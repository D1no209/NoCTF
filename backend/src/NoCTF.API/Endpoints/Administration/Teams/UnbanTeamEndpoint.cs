using FastEndpoints;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class UnbanTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class UnbanTeamEndpoint(ModerateTeam moderate, IUserContext userContext)
    : Endpoint<UnbanTeamRequest>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/unban");
        AuthSchemes("Bearer");
        Roles("Administrator", "Organizer");
    }

    public override async Task HandleAsync(UnbanTeamRequest request, CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        var result = await moderate.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            userContext.UserId,
            false,
            null,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            await HttpContext.Response.SendStatusCodeAsync(StatusCodes.Status400BadRequest, cancellationToken);
            return;
        }
        await HttpContext.Response.SendNoContentAsync(cancellationToken);
    }
}
