using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Teams;

public sealed class UpdateTeamEndpoint(UpdateTeam update, ITeamRegistrationStore store, IUserContext user)
    : Endpoint<UpdateTeamRequest, Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Put("/competitions/{competitionId}/teams/{teamId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<TeamResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(UpdateTeamRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId"); request.TeamId = Route<Guid>("teamId");
        if (!await store.CanManageAsync(user.UserId, request.CompetitionId, request.TeamId, ct)) return TypedResults.Forbid();
        var result = await update.ExecuteAsync(TeamMapper.ToCommand(request), ct);
        if (result.FailureCode == TeamRegistrationFailure.TeamLocked) return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Team was not updated.", detail: result.ErrorMessage);
        return TypedResults.Ok(TeamMapper.ToResponse(result.Value!));
    }
}
