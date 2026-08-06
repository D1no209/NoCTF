using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Profiles;

namespace NoCTF.API.Endpoints.Teams;

public sealed class LeaveTeamEndpoint(LeaveTeamProfile leave, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Delete("/teams/{teamId}/members/me"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await leave.ExecuteAsync(Route<Guid>("teamId"), user.UserId, ct);
        if (result.ErrorCode == "member_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "User could not leave the team.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}
