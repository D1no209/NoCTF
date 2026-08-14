using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class DeleteChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ChallengeFlagFailureResponse>>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionChallengeFlag"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a competition-scoped flag.";
            summary.Description = "Soft-deletes one protected flag from the CompetitionChallenge scope.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult, Conflict<ChallengeFlagFailureResponse>>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await flags.DeleteAsync(
            ChallengeFlagScope.Competition(competitionId, Route<Guid>("competitionChallengeId")),
            Route<Guid>("flagId"),
            actorId: null,
            isAdministrator: true,
            DateTimeOffset.UtcNow,
            ct);
        if (result.Succeeded)
            return TypedResults.NoContent();
        return result.FailureCode == ChallengeFlagFailureCode.SystemManagedFlag
            ? TypedResults.Conflict(ChallengeFlagFailureMapping.ToResponse(result.FailureCode.Value, result.ErrorMessage))
            : TypedResults.NotFound();
    }
}
