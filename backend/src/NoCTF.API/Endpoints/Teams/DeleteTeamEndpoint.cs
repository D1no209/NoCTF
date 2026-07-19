using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class DeleteTeamEndpoint(DeleteTeam delete, ITeamRegistrationStore store, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Delete("/competitions/{competitionId}/teams/{teamId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId"); var teamId = Route<Guid>("teamId");
        if (!await store.CanManageAsync(user.UserId, competitionId, teamId, ct)) return TypedResults.Forbid();
        var result = await delete.ExecuteAsync(competitionId, teamId, user.UserId, DateTimeOffset.UtcNow, ct);
        if (result.ErrorCode == "team_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Team was not deleted.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}
