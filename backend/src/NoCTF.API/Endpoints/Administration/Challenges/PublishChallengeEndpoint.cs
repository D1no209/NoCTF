using FastEndpoints; using Microsoft.AspNetCore.Http; using Microsoft.AspNetCore.Http.HttpResults; using NoCTF.API.Security; using NoCTF.Application.Challenges.Management; using NoCTF.Application.Teams.Moderation;
namespace NoCTF.API.Endpoints.Administration.Challenges;
public sealed class PublishChallengeEndpoint(SetChallengePublished publish, ICompetitionModerationAuthorizer authorizer, IUserContext user) : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/challenges/{challengeId}/publish"); AuthSchemes("Bearer"); }
    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId"); var challengeId = Route<Guid>("challengeId"); if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct)) return TypedResults.Forbid();
        var result = await publish.ExecuteAsync(competitionId, challengeId, true, DateTimeOffset.UtcNow, ct); if (result.ErrorCode is "challenge_not_found" or "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Challenge was not published.", detail: result.ErrorMessage); return TypedResults.NoContent();
    }
}
