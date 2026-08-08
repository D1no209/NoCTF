using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class RemoveTeamMemberEndpoint(RemoveTeamMember remove, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Delete("/competitions/{competitionId}/teams/{teamId}/members/{userId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await remove.ExecuteAsync(Route<Guid>("competitionId"), Route<Guid>("teamId"), Route<Guid>("userId"), user.UserId, ct);
        if (result.FailureCode is TeamMembershipFailure.TeamNotFound or TeamMembershipFailure.MemberNotFound) return TypedResults.NotFound();
        if (result.FailureCode == TeamMembershipFailure.TeamForbidden) return TypedResults.Forbid();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Member was not removed.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}
