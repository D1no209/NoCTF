using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class LeaveTeamEndpoint(LeaveTeam leave, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure() {
        Summary(summary =>
        {
            summary.Summary = "Removes the current user from their competition team when permitted.";
            summary.Description = summary.Summary;
        });
 Delete("/competitions/{competitionId}/teams/me/membership"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var result = await leave.ExecuteAsync(Route<Guid>("competitionId"), user.UserId, ct);
        if (result.FailureCode == TeamMembershipFailure.MembershipNotFound) return TypedResults.NotFound();
        if (!result.Succeeded) return ApiProblems.Problem(statusCode: StatusCodes.Status409Conflict, title: ApiMessages.Get(ApiMessageId.LeaveTeamTitleUserCouldLeaveTeam), detail: ApiMessages.For(result.FailureCode));
        return TypedResults.NoContent();
    }
}
