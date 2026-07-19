using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class CreateChallengeFlagEndpoint(
    CreateChallengeFlag create,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<CreateChallengeFlagRequest,
        Results<Created<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{challengeId}/flags");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Creates a global or Team-specific Challenge Flag.");
    }

    public override async Task<Results<Created<ChallengeFlagSecretResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(CreateChallengeFlagRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.ChallengeId = Route<Guid>("challengeId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();

        var result = await create.ExecuteAsync(
            ChallengeFlagMapper.ToCommand(request, DateTimeOffset.UtcNow),
            ct);
        if (result.ErrorCode is "challenge_not_found" or "team_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: result.ErrorCode is "flag_window_conflict" or "flag_locked"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status400BadRequest,
                title: "Challenge Flag was not created.",
                detail: result.ErrorMessage);

        var response = ChallengeFlagMapper.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/admin/competitions/{request.CompetitionId}/challenges/{request.ChallengeId}/flags/{response.Id}",
            response);
    }
}
