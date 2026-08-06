using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed record RotateTeamInvitationResponse(string InvitationToken);
public sealed class RotateTeamInvitationEndpoint(
    RotateTeamProfileInvitation rotate,
    ITeamProfileStore store,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<RotateTeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/teams/{teamId}/invitation-token/rotate"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<RotateTeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var teamId = Route<Guid>("teamId");
        if (!await store.CanManageAsync(user.UserId, teamId, ct)) return TypedResults.Forbid();
        var result = await rotate.ExecuteAsync(teamId, ct);
        if (result.ErrorCode == "team_not_found") return TypedResults.NotFound();
        if (result.ErrorCode == "team_forbidden") return TypedResults.Forbid();
        return result.Succeeded ? TypedResults.Ok(new RotateTeamInvitationResponse(result.Value!)) : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, detail: result.ErrorMessage);
    }
}
