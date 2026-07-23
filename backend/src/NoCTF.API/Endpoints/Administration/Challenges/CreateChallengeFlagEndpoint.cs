using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class CreateChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<SaveChallengeFlagRequest, Results<Created<ChallengeFlagResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags");
        AuthSchemes("Bearer");
        Summary(summary => summary.Summary = "Creates a competition-scoped flag.");
    }

    public override async Task<Results<Created<ChallengeFlagResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        SaveChallengeFlagRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var result = await flags.SaveAsync(
            SaveChallengeFlagMapping.ToCommand(
                request,
                ChallengeFlagScope.Competition(competitionId, competitionChallengeId),
                null,
                DateTimeOffset.UtcNow),
            actorId: null,
            isAdministrator: true,
            ct);
        if (result.ErrorCode == "flag_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Flag was not created.",
                detail: result.ErrorMessage);
        var response = ChallengeFlagMapping.ToResponse(result.Value!);
        return TypedResults.Created(
            $"/api/v1/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{response.Id}",
            response);
    }
}
