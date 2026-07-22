using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeConfigurationEndpoint(
    UpdateChallengeConfiguration update,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<UpdateChallengeConfigurationRequest,
        Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Updates a challenge configuration using optimistic concurrency.");
    }

    public override async Task<Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
        ExecuteAsync(UpdateChallengeConfigurationRequest request, CancellationToken ct)
    {
        request.CompetitionId = Route<Guid>("competitionId");
        request.CompetitionChallengeId = Route<Guid>("competitionChallengeId");
        if (!await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();

        var result = await update.ExecuteAsync(
            request.CompetitionId,
            request.CompetitionChallengeId,
            request.ExpectedRevision,
            request.Json,
            DateTimeOffset.UtcNow,
            ct);
        if (result.ErrorCode == "challenge_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            var status = result.ErrorCode is "configuration_conflict" or "configuration_locked"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;
            return TypedResults.Problem(
                statusCode: status,
                title: "Challenge configuration was not updated.",
                detail: result.ErrorMessage);
        }

        return TypedResults.Ok(ChallengeConfigurationMapper.ToResponse(result.Value!));
    }
}
