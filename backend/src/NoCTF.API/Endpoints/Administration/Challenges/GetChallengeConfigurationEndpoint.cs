using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Configuration;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class GetChallengeConfigurationEndpoint(
    GetChallengeConfiguration get,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/configuration");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Gets a challenge's versioned game-mode configuration.");
    }

    public override async Task<Results<Ok<ChallengeConfigurationResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var view = await get.ExecuteAsync(competitionId, Route<Guid>("competitionChallengeId"), ct);
        return view is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ChallengeConfigurationMapper.ToResponse(view));
    }
}
