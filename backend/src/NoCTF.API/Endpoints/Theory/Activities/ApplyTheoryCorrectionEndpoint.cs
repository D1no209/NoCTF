using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class ApplyTheoryCorrectionRequest
{
    public Guid ActivityId { get; set; } public required TheoryCorrectionInput Input { get; set; }
    public Guid Token { get; set; } public DateTimeOffset ExpiresAt { get; set; } public required string Reason { get; set; }
}
public sealed class ApplyTheoryCorrectionValidator : Validator<ApplyTheoryCorrectionRequest>
{
    public ApplyTheoryCorrectionValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Input).NotNull();
        RuleFor(request => request.Token).NotEmpty(); RuleFor(request => request.Reason).NotEmpty().MaximumLength(2000);
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new TheoryCorrectionValidator())); }
}
public sealed record TheoryCorrectionAccepted(Guid TargetRevision, string StatusUrl);

public sealed class ApplyTheoryCorrectionEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<ApplyTheoryCorrectionRequest, Results<Accepted<TheoryCorrectionAccepted>, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/theory/activities/{activityId}/corrections"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("ApplyTheoryCorrection")); }
    public override async Task<Results<Accepted<TheoryCorrectionAccepted>, NotFound, ProblemHttpResult>> ExecuteAsync(ApplyTheoryCorrectionRequest request, CancellationToken ct)
        {
        var result = await store.CorrectAsync(user.UserId, request.ActivityId, request.Input, request.Token, request.ExpiresAt, request.Reason, clock.GetUtcNow(), ct);
        return result.TargetRevision is { } target ? TypedResults.Accepted($"/api/v1/theory/activities/{request.ActivityId}/management",
            new TheoryCorrectionAccepted(target, $"/api/v1/theory/activities/{request.ActivityId}/management"))
            : result.Failure == TheoryReviewFailure.NotFoundOrForbidden ? TypedResults.NotFound() : TheoryActivityHttp.Problem(result.Failure!);
    }
}
