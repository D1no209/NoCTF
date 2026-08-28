using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class DeleteChallengeHintEndpoint(
    ManageChallengeHints hints,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/hints/{hintId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionChallengeHint"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a competition challenge hint.";
            summary.Description = "Soft-deletes one hint owned by the CompetitionChallenge.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();
        var result = await hints.DeleteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            Route<Guid>("hintId"),
            timeProvider.GetUtcNow(),
            ct);
        return result.Succeeded ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
