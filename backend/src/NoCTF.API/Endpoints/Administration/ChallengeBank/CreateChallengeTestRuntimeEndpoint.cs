using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.API.Endpoints.Runtime;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed record ChallengeTestRuntimeAcceptedResponse(
    Guid RuntimeInstanceId,
    string StatusUrl);

public sealed class CreateChallengeTestRuntimeRequest
{
    public Guid? ReplacesRuntimeId { get; set; }
}

public sealed class CreateChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    GetChallengeTestRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<CreateChallengeTestRuntimeRequest,
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/test-runtimes");
        AuthSchemes("Bearer");
        Roles("Organizer", "Administrator");
        Options(builder => builder.WithMetadata(new ProtectedEntryMetadata(ProtectedEntry.RuntimeCommand)));
        Description(builder => builder.WithName("AdminChallengeBankCreateTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Creates or replaces a challenge-template test Runtime.");
    }

    public override async Task<Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound,
        Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        CreateChallengeTestRuntimeRequest request,
        CancellationToken ct)
    {
        var challengeId = Route<Guid>("challengeId");
        if (request.ReplacesRuntimeId is Guid expected)
        {
            var current = await get.ExecuteAsync(
                challengeId,
                user.UserId,
                user.IsAdministrator,
                ct);
            if (current?.Id != expected)
                return TypedResults.Conflict(new RuntimeConflictResponse(
                    "ReplacesRuntimeId does not identify the current test Runtime."));
        }
        var result = await ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            challengeId,
            request.ReplacesRuntimeId is null ? RuntimeAction.Start : RuntimeAction.Reset,
            null,
            timeProvider,
            ct);
        return result;
    }
}

internal static class ChallengeTestRuntimeMutationEndpoint
{
    public static async Task<Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound,
        Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        MutateChallengeTestRuntime mutate,
        IUserContext user,
        Guid challengeId,
        RuntimeAction action,
        TimeSpan? extension,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var result = await mutate.ExecuteAsync(new(
            challengeId,
            user.UserId,
            user.IsAdministrator,
            action,
            extension,
            timeProvider.GetUtcNow()), ct);
        if (result.Failure == RuntimeMutationFailure.NotFound)
            return TypedResults.NotFound();
        if (result.Runtime is null)
        {
            if (result.Failure is RuntimeMutationFailure.InvalidState or RuntimeMutationFailure.Conflict)
                return TypedResults.Conflict(new RuntimeConflictResponse(
                    "The test Runtime changed while the operation was processed."));
            return TypedResults.Problem(
                statusCode: result.Failure == RuntimeMutationFailure.CapacityExceeded
                    ? StatusCodes.Status503ServiceUnavailable
                    : StatusCodes.Status409Conflict,
                title: "Challenge test Runtime operation was rejected.",
                detail: result.Failure.ToString());
        }
        var statusUrl = $"/api/v1/admin/challenges/{challengeId}/test-runtimes/current";
        return TypedResults.Accepted(
            statusUrl,
            new ChallengeTestRuntimeAcceptedResponse(result.Runtime.Id, statusUrl));
    }
}
