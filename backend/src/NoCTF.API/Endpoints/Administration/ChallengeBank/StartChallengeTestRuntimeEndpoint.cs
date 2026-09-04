using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeTestRuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

public sealed class StartChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/test-runtime/start");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankStartTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary =>
        {
            summary.Summary = "Queues a challenge-template test Runtime.";
            summary.Description =
                "Uses the saved runtime definition and normal Runner provisioning path, including dynamic Flag delivery.";
        });
    }

    public override Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken) =>
        ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            Route<Guid>("challengeId"),
            RuntimeAction.Start,
            null,
            timeProvider,
            cancellationToken);
}

internal static class ChallengeTestRuntimeMutationEndpoint
{
    public static async Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        MutateChallengeTestRuntime mutate,
        IUserContext user,
        Guid challengeId,
        RuntimeAction action,
        TimeSpan? extension,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var result = await mutate.ExecuteAsync(new(
            challengeId,
            user.UserId,
            user.IsAdministrator,
            action,
            extension,
            timeProvider.GetUtcNow()), cancellationToken);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
        {
            var statusCode = result.Failure == RuntimeMutationFailure.CapacityExceeded
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status409Conflict;
            var detail = result.Failure switch
            {
                RuntimeMutationFailure.Unsupported =>
                    "The challenge template does not define a supported Container or Compose runtime.",
                RuntimeMutationFailure.ConfigurationInvalid =>
                    "The saved runtime or dynamic Flag configuration is invalid. Save a valid definition before testing.",
                RuntimeMutationFailure.CapacityExceeded =>
                    "No Runner currently has enough capacity for this test Runtime.",
                RuntimeMutationFailure.InvalidState =>
                    "The current test Runtime state does not allow this operation. Refresh its status and retry.",
                _ =>
                    "The test Runtime changed while the operation was processed. Refresh its status and retry."
            };
            return TypedResults.Problem(
                statusCode: statusCode,
                title: "Challenge test Runtime operation was rejected.",
                detail: detail);
        }

        var statusUrl = $"/api/v1/admin/challenges/{challengeId}/test-runtime";
        return TypedResults.Accepted(
            statusUrl,
            new ChallengeTestRuntimeAcceptedResponse(result.Runtime.Id, statusUrl));
    }
}
