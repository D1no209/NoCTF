using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class DeleteChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class DeleteChallengeEndpoint(
    DeleteChallenge delete,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<DeleteChallengeRequest,
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Deletes a competition challenge.";
            summary.Description =
                "Soft-deletes the competition link without changing the global challenge template.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        DeleteChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await delete.ExecuteAsync(
            competitionId,
            request.CompetitionChallengeId,
            timeProvider.GetUtcNow(),
            ct);
        return result switch
        {
            null => TypedResults.NoContent(),
            ChallengeMutationFailure.CompetitionNotFound
                or ChallengeMutationFailure.ChallengeNotFound =>
                TypedResults.NotFound(),
            ChallengeMutationFailure.LifecycleStateConflict =>
                TypedResults.Conflict(
                    CompetitionChallengeConflictMapper.ToResponse(result.Value)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition challenge delete failure: {result}.")
        };
    }
}
