using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Challenges.Testing;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Administration.ChallengeBank;

public sealed class ExtendChallengeTestRuntimeRequest
{
    public int Seconds { get; set; }
}

public sealed class ExtendChallengeTestRuntimeValidator : Validator<ExtendChallengeTestRuntimeRequest>
{
    public ExtendChallengeTestRuntimeValidator()
    {
        RuleFor(request => request.Seconds).InclusiveBetween(60, 86_400);
    }
}

public sealed class ExtendChallengeTestRuntimeEndpoint(
    MutateChallengeTestRuntime mutate,
    IUserContext user,
    TimeProvider timeProvider)
    : Endpoint<ExtendChallengeTestRuntimeRequest,
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/challenges/{challengeId}/test-runtime/extend");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Security.ProtectedEntryMetadata(NoCTF.API.Security.ProtectedEntry.RuntimeCommand)));
        Roles("Organizer", "Administrator");
        Description(builder => builder.WithName("AdminChallengeBankExtendTestRuntime")
            .ProducesProblemFE(StatusCodes.Status409Conflict)
            .ProducesProblemFE(StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Extends a nearly expired challenge-template test Runtime.");
    }

    public override Task<
        Results<Accepted<ChallengeTestRuntimeAcceptedResponse>, NotFound, ProblemHttpResult>> ExecuteAsync(
        ExtendChallengeTestRuntimeRequest request,
        CancellationToken cancellationToken) =>
        ChallengeTestRuntimeMutationEndpoint.ExecuteAsync(
            mutate,
            user,
            Route<Guid>("challengeId"),
            RuntimeAction.Extend,
            TimeSpan.FromSeconds(request.Seconds),
            timeProvider,
            cancellationToken);
}
