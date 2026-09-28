using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.API.Endpoints.Runtime;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class StopChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    GetChallengeTestRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Delete("/admin/challenges/{challengeId}/test-runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(builder => builder.WithMetadata(
            new NoCTF.Hosting.Observability.RuntimeOperationMetricsMetadata(
                NoCTF.Application.Observability.RuntimeOperationMetricKind.TestStop)));
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankStopTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Stops the current challenge-template test Runtime.");
    }

    public override async Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var challengeId = Route<Guid>("challengeId");
        var current = await get.ExecuteAsync(challengeId, user.UserId, user.IsAdministrator, cancellationToken);
        if (current?.Id != Route<Guid>("runtimeInstanceId"))
            return TypedResults.NotFound();
        return await ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            challengeId,
            RuntimeAction.Stop,
            null,
            timeProvider,
            cancellationToken);
    }
}
