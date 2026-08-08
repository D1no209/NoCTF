using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Runtime.Instances;

namespace NoCTF.API.Endpoints.Runtime;

public sealed class ExtendRuntimeRequest
{
    public int Seconds { get; set; }
}

public sealed class ExtendRuntimeValidator : Validator<ExtendRuntimeRequest>
{
    public ExtendRuntimeValidator() =>
        RuleFor(request => request.Seconds).InclusiveBetween(1, 86_400);
}

public sealed class ExtendRuntimeEndpoint(
    MutatePlayerRuntime mutate,
    IUserContext user)
    : Endpoint<ExtendRuntimeRequest, Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/competitions/{competitionId}/challenges/{competitionChallengeId}/runtime/extend");
        AuthSchemes("Bearer");
        Options(options => options
            .ProducesProblemFE<Microsoft.AspNetCore.Mvc.ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable));
        Summary(summary => summary.Summary = "Extends a running CTF runtime.");
    }

    public override async Task<Results<Accepted<RuntimeAcceptedResponse>, NotFound, Conflict, ProblemHttpResult>> ExecuteAsync(
        ExtendRuntimeRequest request,
        CancellationToken ct)
    {
        var result = await mutate.ExecuteAsync(new RuntimeMutationCommand(
            Route<Guid>("competitionId"),
            Route<Guid>("competitionChallengeId"),
            user.UserId,
            RuntimeAction.Extend,
            TimeSpan.FromSeconds(request.Seconds),
            DateTimeOffset.UtcNow), ct);
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeNotFound or RuntimeMutationFailureCode.RuntimeActionUnsupported)
            return TypedResults.NotFound();
        if (result.FailureCode is RuntimeMutationFailureCode.RuntimeStateConflict or RuntimeMutationFailureCode.RuntimeConflict)
            return TypedResults.Conflict();
        if (!result.Succeeded)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Runtime could not be extended.",
                detail: result.ErrorMessage);
        var accepted = RuntimeEndpointMapping.ToAccepted(result.Value!);
        return TypedResults.Accepted(accepted.StatusUrl, accepted);
    }
}
