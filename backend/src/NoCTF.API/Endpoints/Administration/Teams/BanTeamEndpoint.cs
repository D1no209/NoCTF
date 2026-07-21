using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Teams;

public sealed class BanTeamRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class BanTeamEndpoint(
    ModerateTeam moderate,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext userContext)
    : Endpoint<BanTeamRequest, Results<NoContent, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/teams/{teamId}/ban");
        AuthSchemes("Bearer");
        Description(builder => builder.ProducesProblemFE(StatusCodes.Status404NotFound)
            .ProducesProblemFE(StatusCodes.Status409Conflict));
    }

    public override async Task<Results<NoContent, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        BanTeamRequest request,
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
            true,
            request.Reason,
            DateTimeOffset.UtcNow), cancellationToken);
        if (!result.Succeeded)
        {
            var statusCode = result.ErrorCode == "competition_finished"
                ? StatusCodes.Status409Conflict
                : result.ErrorCode is "competition_not_found" or "team_not_found"
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(statusCode: statusCode,
                title: "Team ban was rejected.", detail: result.ErrorMessage);
        }
        return TypedResults.NoContent();
    }
}
