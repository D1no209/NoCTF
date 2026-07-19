using FastEndpoints; using Microsoft.AspNetCore.Http; using Microsoft.AspNetCore.Http.HttpResults; using NoCTF.API.Endpoints.Challenges; using NoCTF.API.Security; using NoCTF.Application.Challenges.Management; using NoCTF.Application.Teams.Moderation;
namespace NoCTF.API.Endpoints.Administration.Challenges;
public sealed class UpdateChallengeEndpoint(UpdateChallenge update, ICompetitionModerationAuthorizer authorizer, IUserContext user) : Endpoint<UpdateChallengeRequest, Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure() { Put("/admin/competitions/{competitionId}/challenges/{challengeId}"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<ChallengeResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(UpdateChallengeRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId"); request.ChallengeId = Route<Guid>("challengeId"); if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct)) return TypedResults.Forbid();
        var result = await update.ExecuteAsync(ChallengeMapper.ToCommand(request, DateTimeOffset.UtcNow), ct); if (result.ErrorCode is "challenge_not_found" or "competition_not_found") return TypedResults.NotFound();
        if (!result.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Challenge was not updated.", detail: result.ErrorMessage); return TypedResults.Ok(ChallengeMapper.ToResponse(result.Value!));
    }
}
