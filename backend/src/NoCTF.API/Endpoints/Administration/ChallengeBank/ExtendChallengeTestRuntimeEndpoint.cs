using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;
using NoCTF.API.Endpoints.Runtime;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class ExtendChallengeTestRuntimeRequest
{
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ExtendChallengeTestRuntimeValidator : Validator<ExtendChallengeTestRuntimeRequest>
{
    public ExtendChallengeTestRuntimeValidator()
    {
        RuleFor(request => request.ExpiresAt).NotEmpty();
    }
}

public sealed class ExtendChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    GetChallengeTestRuntime get,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ExtendChallengeTestRuntimeRequest,
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Patch("/admin/challenges/{challengeId}/test-runtimes/{runtimeInstanceId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankExtendTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Extends a nearly expired challenge-template test Runtime.");
    }

    public override async Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, Conflict<RuntimeConflictResponse>, ProblemHttpResult>> ExecuteAsync(
        ExtendChallengeTestRuntimeRequest request,
        CancellationToken cancellationToken)
    {
        var challengeId = Route<Guid>("challengeId");
        var current = await get.ExecuteAsync(challengeId, user.UserId, user.IsAdministrator, cancellationToken);
        if (current?.Id != Route<Guid>("runtimeInstanceId") || current.ExpiresAt is null)
            return TypedResults.NotFound();
        return await ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            challengeId,
            RuntimeAction.Extend,
            request.ExpiresAt - current.ExpiresAt.Value,
            timeProvider,
            cancellationToken);
    }
}
