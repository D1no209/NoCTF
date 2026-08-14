using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Endpoints.Administration.ChallengeBank;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Flags;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class UpdateChallengeFlagEndpoint(
    ManageChallengeFlags flags,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<SaveChallengeFlagRequest, Results<
        Ok<ChallengeFlagResponse>,
        NotFound,
        ForbidHttpResult,
        Conflict<ChallengeFlagFailureResponse>>>
{
    public override void Configure()
    {
        Put("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/flags/{flagId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateCompetitionChallengeFlag"));
        Summary(summary =>
        {
            summary.Summary = "Updates a competition-scoped flag.";
            summary.Description = "Replaces protected flag material and validity metadata within one CompetitionChallenge.";
        });
    }

    public override async Task<Results<
        Ok<ChallengeFlagResponse>,
        NotFound,
        ForbidHttpResult,
        Conflict<ChallengeFlagFailureResponse>>> ExecuteAsync(
        SaveChallengeFlagRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await flags.SaveAsync(
            SaveChallengeFlagMapping.ToCommand(
                request,
                ChallengeFlagScope.Competition(competitionId, Route<Guid>("competitionChallengeId")),
                Route<Guid>("flagId"),
                isCreate: false,
                DateTimeOffset.UtcNow),
            actorId: null,
            isAdministrator: true,
            ct);
        if (result.FailureCode == ChallengeFlagFailureCode.FlagNotFound)
            return TypedResults.NotFound();
        if (result.Succeeded)
            return TypedResults.Ok(ChallengeFlagMapping.ToResponse(result.Value!));
        var failure = ChallengeFlagFailureMapping.ToResponse(result.FailureCode!.Value, result.ErrorMessage);
        return TypedResults.Conflict(failure);
    }
}
