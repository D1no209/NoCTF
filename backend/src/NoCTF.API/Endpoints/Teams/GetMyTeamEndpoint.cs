using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetMyTeamRequest { public Guid CompetitionId { get; set; } }

public sealed class GetMyTeamEndpoint(GetMyTeam get, IUserContext user, LinkGenerator links)
    : Endpoint<GetMyTeamRequest, Results<Ok<TeamResponse>, NotFound>>
{
    public override void Configure()
    {
        Get("/competitions/{competitionId}/teams/me");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<TeamResponse>, NotFound>> ExecuteAsync(
        GetMyTeamRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        var team = await get.ExecuteAsync(request.CompetitionId, user.UserId, false, cancellationToken);
        return team is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TeamMapper.ToResponse(team, links, HttpContext));
    }
}
