using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class DeleteChallengeEndpoint(
    DeleteChallenge delete,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : EndpointWithoutRequest<Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionChallenge")
            .ProducesProblemFE(StatusCodes.Status409Conflict));
        Summary(summary =>
        {
            summary.Summary = "Deletes a competition challenge.";
            summary.Description = "Soft-deletes the competition link without changing the global challenge template.";
        });
    }

    public override async Task<
        Results<NoContent, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await delete.ExecuteAsync(
            competitionId,
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            DateTimeOffset.UtcNow,
            ct);
        if (result.ErrorCode is "competition_not_found" or "competition_challenge_not_found")
            return TypedResults.NotFound();
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Challenge was not deleted.",
                detail: result.ErrorMessage);
        }

        return TypedResults.NoContent();
    }
}
