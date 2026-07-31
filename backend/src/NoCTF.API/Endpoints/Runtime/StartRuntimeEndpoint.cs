using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class StartRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    IUserContext user)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start");
        AuthSchemes("Bearer");
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Queues a team runtime start.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        RuntimeMutationEndpoint.ExecuteAsync(
            mutate, user, Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            RuntimeAction.Start, null, ct);
}

internal static class RuntimeMutationEndpoint
{
    public static async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ProblemHttpResult>> ExecuteAsync(
        MutatePlayerRuntime mutate,
        IUserContext user,
        Guid competitionId,
        Guid competitionChallengeId,
        RuntimeAction action,
        TimeSpan? extension,
        CancellationToken ct)
    {
        var result = await mutate.ExecuteAsync(new RuntimeMutationCommand(
            competitionId,
            competitionChallengeId,
            user.UserId,
            action,
            extension,
            DateTimeOffset.UtcNow), ct);
        if (result.ErrorCode is "runtime_not_found" or "runtime_action_unsupported")
            return TypedResults.NotFound();
        if (result.ErrorCode is "runtime_state_conflict" or "runtime_conflict")
            return TypedResults.Conflict();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Runtime action could not be queued.",
                detail: result.ErrorMessage);
        var accepted = RuntimeEndpointMapping.ToAccepted(result.Value!);
        return TypedResults.Accepted(accepted.StatusUrl, accepted);
    }
}
