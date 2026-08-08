using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetTeamRequest { public Guid CompetitionId { get; set; } public Guid TeamId { get; set; } }

public sealed class GetTeamEndpoint(GetTeam get, LinkGenerator links) : Endpoint<GetTeamRequest, Results<Ok<TeamResponse>, NotFound>>
{
    public override void Configure() { Get("/competitions/{competitionId}/teams/{teamId}"); AllowAnonymous(); }
    public override async Task<Results<Ok<TeamResponse>, NotFound>> ExecuteAsync(GetTeamRequest request, CancellationToken ct)
    {
        var team = await get.ExecuteAsync(Route<Guid>("competitionId"), Route<Guid>("teamId"), false, ct);
        return team is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TeamMapper.ToResponse(team, links, HttpContext));
    }
}
