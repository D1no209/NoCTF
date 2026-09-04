using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class StartRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/start");
        AuthSchemes("Bearer");
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status409Conflict)
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Queues a team runtime start.");
    }

    public override Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        CancellationToken ct) =>
        RuntimeMutationEndpoint.ExecuteAsync(
            mutate, user, Route<Guid>("competitionId"), Route<Guid>("competitionChallengeId"),
            RuntimeAction.Start, null, timeProvider, ct);
}

internal static class RuntimeMutationEndpoint
{
    public static async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        MutatePlayerRuntime mutate,
        IUserContext user,
        Guid competitionId,
        Guid competitionChallengeId,
        RuntimeAction action,
        TimeSpan? extension,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var result = await mutate.ExecuteAsync(new RuntimeMutationCommand(
            competitionId,
            competitionChallengeId,
            user.UserId,
            action,
            extension,
            timeProvider.GetUtcNow()), ct);
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeNotFound or RuntimeMutationFailureCode.RuntimeActionUnsupported)
            return TypedResults.NotFound();
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeStateConflict or RuntimeMutationFailureCode.RuntimeConflict)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Runtime operation conflicts with its current state.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode.Value.ToString()
                });
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Runtime action could not be queued.",
                detail: result.ErrorMessage);
        var accepted = RuntimeEndpointMapping.ToAccepted(result.Value!);
        return TypedResults.Accepted(accepted.StatusUrl, accepted);
    }
}
