using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Admission;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.Runtime;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class CreateRuntimeRequest
{
    public Guid? ReplacesRuntimeId { get; set; }
}

public sealed class CreateRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    GetPlayerRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateRuntimeRequest,
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtimes");
        AuthSchemes("Bearer");
        Options(builder => builder
            .WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand))
            .WithMetadata(new HumanVerificationMetadata(HumanVerificationAction.Runtime)));
        Description(builder => builder
            .WithName("CreateRuntime")
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary =>
        {
            summary.Summary = "Creates or atomically replaces the current team runtime.";
            summary.Description =
                "ReplacesRuntimeId must identify the current runtime when a replacement is requested.";
        });
    }

    public override async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateRuntimeRequest request,
        CancellationToken ct)
    {
        var competitionId = Route<Guid>("competitionId");
        var competitionChallengeId = Route<Guid>("competitionChallengeId");
        var current = await get.ExecuteAsync(
            competitionId,
            competitionChallengeId,
            user.UserId,
            ct);
        var startConflicts = request.ReplacesRuntimeId is null
            && current is { State: not (RuntimeState.Stopped or RuntimeState.Failed) };
        var resetConflicts = request.ReplacesRuntimeId is Guid expected
            && current?.Id != expected;
        if (startConflicts || resetConflicts)
        {
            return TypedResults.Conflict(new RuntimeConflictResponse(
                "ReplacesRuntimeId does not match the current runtime state."));
        }

        return await PlayerRuntimeMutation.ExecuteAsync(
            mutate,
            user,
            competitionId,
            competitionChallengeId,
            request.ReplacesRuntimeId is null ? RuntimeAction.Start : RuntimeAction.Reset,
            null,
            timeProvider,
            ct);
    }
}

internal static class PlayerRuntimeMutation
{
    public static async Task<
        Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
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
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeNotFound
            or RuntimeMutationFailureCode.RuntimeActionUnsupported)
            return TypedResults.NotFound();
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeStateConflict
            or RuntimeMutationFailureCode.RuntimeConflict)
            return TypedResults.Conflict(new RuntimeConflictResponse(
                result.ErrorMessage ?? "Runtime state changed while the operation was processed."));
        if (!result.Succeeded)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Runtime operation could not be queued.",
                detail: result.ErrorMessage,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = result.FailureCode?.ToString()
                });
        }
        var accepted = RuntimeEndpointMapping.ToAccepted(result.Value!);
        return TypedResults.Accepted(accepted.StatusUrl, accepted);
    }
}
