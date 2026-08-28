using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class RestoreChallengeTemplateEndpoint(
    RestoreChallengeTemplate restore,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<
            NoContent,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/restore");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminChallengeBankRestoreTemplate"));
        Summary(summary =>
        {
            summary.Summary = "Restores a soft-deleted global challenge template.";
            summary.Description =
                "Restores a reusable template for its owner, a manager, or a platform administrator.";
        });
    }

    public override async Task<
        Results<
            NoContent,
            NotFound,
            Conflict<ChallengeTemplateConflictResponse>>> ExecuteAsync(CancellationToken ct)
    {
        var result = await restore.ExecuteAsync(
            Route<Guid>("challengeId"),
            user.UserId,
            user.IsAdministrator,
            timeProvider.GetUtcNow(),
            ct);
        return result.State switch
        {
            ChallengeTemplateWriteState.Succeeded => TypedResults.NoContent(),
            ChallengeTemplateWriteState.NotFoundOrForbidden => TypedResults.NotFound(),
            ChallengeTemplateWriteState.UserNotFound
                or ChallengeTemplateWriteState.RoleNotEligible =>
                TypedResults.Conflict(
                    ChallengeTemplateWriteResponseMapper.ToConflict(result)),
            _ => throw new InvalidOperationException(
                $"Unsupported challenge template restore state: {result.State}.")
        };
    }
}
