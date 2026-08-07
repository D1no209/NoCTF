using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Application.Teams.Registration;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class RejectTeamEndpoint(ReviewTeamRegistration review, ICompetitionModerationAuthorizer authorizer, IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/reject");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRejectTeam")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Rejects a pending team.";
            summary.Description = "Rejects team registration while the competition still permits review changes.";
        });
    }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId"); var teamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await review.ExecuteAsync(competitionId, teamId, false, ct);
        if (result.FailureCode == TeamRegistrationFailure.TeamNotFound) return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Team was not rejected.", detail: result.ErrorMessage);
        return TypedResults.NoContent();
    }
}
