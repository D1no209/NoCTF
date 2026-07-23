using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<SaveChallengeHintRequest,
        Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Updates a competition challenge hint.");
    }

    public override async Task<Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        SaveChallengeHintRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await hints.SaveAsync(new(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            request.Content,
            request.Cost,
            request.PublishedAt,
            DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode == "hint_not_found")
            return TypedResults.NotFound();
        return result.Succeeded
            ? TypedResults.Ok(ChallengeHintMapping.ToResponse(result.Value!))
            : TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Hint was not updated.",
                detail: result.ErrorMessage);
    }
}
