using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.Challenges;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;

namespace NoCTF.API.Endpoints.Challenges;

public sealed class UnlockChallengeHintEndpoint(
    UnlockChallengeHint unlock,
    IUserContext user)
    : EndpointWithoutRequest<
        Results<Created<ChallengeHintResponse>, Ok<ChallengeHintResponse>, NotFound, Conflict>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}/unlock");
        AuthSchemes("Bearer");
        Summary(summary =>
        {
            summary.Summary = "Unlocks a published hint.";
            summary.Description = "The first paid unlock creates a HintUnlock fact from an authoritative database projection.";
        });
    }

    public override async Task<Results<Created<ChallengeHintResponse>, Ok<ChallengeHintResponse>, NotFound, Conflict>> ExecuteAsync(
        CancellationToken ct)
    {
        var result = await unlock.ExecuteAsync(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            user.UserId,
            DateTimeOffset.UtcNow,
            ct);
        if (result.ErrorCode == "hint_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Conflict();
        var response = ChallengeHintMapping.ToResponse(result.Value!.Hint);
        return result.Value.Created
            ? TypedResults.Created(HttpContext.Request.Path, response)
            : TypedResults.Ok(response);
    }
}
