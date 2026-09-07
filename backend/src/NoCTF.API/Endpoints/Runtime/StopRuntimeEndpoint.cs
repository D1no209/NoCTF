using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class StopRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/stop");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Queues a team runtime stop.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        RuntimeMutationEndpoint.ExecuteAsync(
            mutate, user, Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            RuntimeAction.Stop, null, timeProvider, ct);
}
