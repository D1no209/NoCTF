using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ResetRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/reset");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Queues an atomic replacement runtime.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        RuntimeMutationEndpoint.ExecuteAsync(
            mutate, user, Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            RuntimeAction.Reset, null, timeProvider, ct);
}
