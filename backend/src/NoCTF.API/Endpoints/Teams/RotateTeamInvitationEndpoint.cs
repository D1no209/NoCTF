using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed record RotateTeamInvitationResponse(string InvitationToken);
public sealed class RotateTeamInvitationEndpoint(RotateTeamInvitation rotate, IUserContext user)
    : EndpointWithoutRequest<Results<Ok<RotateTeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/competitions/{competitionId}/teams/{teamId}/invitation-token/rotate"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<RotateTeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await rotate.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("teamId"),
            user.UserId,
            ct);
        if (result.FailureCode == TeamMembershipFailure.TeamNotFound) return TypedResults.NotFound();
        if (result.FailureCode == TeamMembershipFailure.TeamForbidden) return TypedResults.Forbid();
        return result.Succeeded ? TypedResults.Ok(new RotateTeamInvitationResponse(result.Value!)) : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: result.ErrorMessage);
    }
}
