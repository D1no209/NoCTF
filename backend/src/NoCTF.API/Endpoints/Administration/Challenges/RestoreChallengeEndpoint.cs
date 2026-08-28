using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Teams.Moderation;

namespace NoCTF.API.Endpoints.Administration.Challenges;

public sealed class RestoreChallengeRequest
{
    public Guid CompetitionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
}

public sealed class RestoreChallengeEndpoint(
    DeleteChallenge restore,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<RestoreChallengeRequest,
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRestoreCompetitionChallenge"));
        Summary(summary =>
        {
            summary.Summary = "Restores a deleted competition challenge.";
            summary.Description =
                "Restores the competition link; the global template is not modified.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            ForbidHttpResult,
            Conflict<CompetitionChallengeConflictResponse>,
            ProblemHttpResult>> ExecuteAsync(
        RestoreChallengeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        if (!await authorizer.CanModerateAsync(user.UserId, competitionId, ct))
            return TypedResults.Forbid();

        var result = await restore.RestoreAsync(
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
            ChallengeMutationFailure.LifecycleStateConflict
                or ChallengeMutationFailure.TemplateNotFound
                or ChallengeMutationFailure.TemplateModeMismatch
                or ChallengeMutationFailure.ChallengeOrderConflict
                or ChallengeMutationFailure.ChallengeTemplateConflict =>
                TypedResults.Conflict(
                    CompetitionChallengeConflictMapper.ToResponse(result.Value)),
            _ => throw new InvalidOperationException(
                $"Unsupported competition challenge restore failure: {result}.")
        };
    }
}
