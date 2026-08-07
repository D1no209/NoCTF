using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class UnbanTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
}

public sealed class UnbanTeamEndpoint(
    ModerateTeam moderate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext userContext)
    : Endpoint<UnbanTeamRequest, Results<NoContent, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/unban");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUnbanTeam")
            .ProducesProblemFE(StatusCodes.Status404NotFound)
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Unbans a competition team.";
            summary.Description = "Restores the team's historical scoring eligibility and normal runtime lifecycle.";
        });
    }

    public override async Task<Results<NoContent, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        UnbanTeamRequest request,
        CancellationToken cancellationToken)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.TeamId = Route<Guid>("teamId");
        if (!await authorizer.CanModerateAsync(userContext.UserId, request.CompetitionId, cancellationToken))
            return TypedResults.Forbid();
        var result = await moderate.ExecuteAsync(new(
            request.CompetitionId,
            request.TeamId,
            userContext.UserId,
            false,
            null,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.FailureCode == TeamModerationFailure.CompetitionFinished
                ? StatusCodes.Status409Conflict
                : result.FailureCode is TeamModerationFailure.CompetitionNotFound or TeamModerationFailure.TeamNotFound
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(statusCode: statusCode,
                title: "Team unban was rejected.", detail: result.ErrorMessage);
        }
        return TypedResults.NoContent();
    }
}
