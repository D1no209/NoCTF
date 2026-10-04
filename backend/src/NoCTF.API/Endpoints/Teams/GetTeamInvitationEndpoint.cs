using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Membership;

namespace NoCTF.API.Endpoints.Teams;

public sealed class GetTeamInvitationRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed record GetTeamInvitationResponse(string InvitationToken);

public sealed class GetTeamInvitationEndpoint(GetTeamInvitation get, IUserContext user)
    : Endpoint<GetTeamInvitationRequest,
        Results<Ok<GetTeamInvitationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Returns the current team invitation token to an authorized captain.";
            summary.Description = summary.Summary;
        });

        Get("/competitions/{competitionId}/teams/{teamId}/invitation-token");
        AuthSchemes("Bearer");
    }

    public override async Task<Results<Ok<GetTeamInvitationResponse>, NotFound,
        ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        GetTeamInvitationRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await get.ExecuteAsync(
            request.CompetitionId,
            request.TeamId,
            user.UserId,
            cancellationToken);
        if (result.FailureCode is TeamMembershipFailure.CompetitionNotFound
            or TeamMembershipFailure.TeamNotFound)
            return TypedResults.NotFound();
        if (result.FailureCode == TeamMembershipFailure.TeamForbidden)
            return TypedResults.Forbid();
        return result.Succeeded
            ? TypedResults.Ok(new GetTeamInvitationResponse(result.Value!))
            : ApiProblems.Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: ApiMessages.For(result.FailureCode));
    }
}
