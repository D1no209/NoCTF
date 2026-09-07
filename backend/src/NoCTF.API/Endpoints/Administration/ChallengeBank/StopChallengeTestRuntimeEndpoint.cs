using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class StopChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/test-runtime/stop");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankStopTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Stops the current challenge-template test Runtime.");
    }

    public override Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken cancellationToken) =>
        ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            Route<Guid>("challengeId"),
            RuntimeAction.Stop,
            null,
            timeProvider,
            cancellationToken);
}
