using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeFlagEndpoint(
    UpdateChallengeFlag update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateChallengeFlagRequest,
        Results<Ok<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{challengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Updates a Challenge Flag using optimistic concurrency.");
    }

    public override async Task<Results<Ok<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(UpdateChallengeFlagRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.ChallengeId = Route<Guid>("challengeId");
        request.FlagId = Route<Guid>("flagId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(
            ChallengeFlagMapper.ToCommand(request, DateTimeOffset.UtcNow),
            ct);
        if (result.ErrorCode is "challenge_not_found" or "team_not_found" or "flag_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: result.ErrorCode is "flag_conflict" or "flag_window_conflict" or "flag_locked"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Challenge Flag was not updated.",
                detail: result.ErrorMessage);

        return TypedResults.Ok(ChallengeFlagMapper.ToResponse(result.Value!));
    }
}
