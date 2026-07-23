using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets a competition challenge hint.");
    }

    public override async Task<Results<Ok<ChallengeHintResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanObserveAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await hints.GetAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            ct);
        return result is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeHintMapping.ToResponse(result));
    }
}
