using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class PreviewTheoryCorrectionRequest { public Guid ActivityId { get; set; } public required TheoryCorrectionInput Input { get; set; } }
public sealed class PreviewTheoryCorrectionValidator : Validator<PreviewTheoryCorrectionRequest>
{
    public PreviewTheoryCorrectionValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Input).NotNull();
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new TheoryCorrectionValidator())); }
}
public sealed class TheoryCorrectionValidator : AbstractValidator<TheoryCorrectionInput>
{
    public TheoryCorrectionValidator() { RuleFor(input => input.RevisionId).NotEmpty(); RuleFor(input => input.Mode).IsInEnum(); RuleFor(input => input.CorrectOptions).NotNull(); }
}

public sealed class PreviewTheoryCorrectionEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<PreviewTheoryCorrectionRequest, Results<Ok<TheoryCorrectionPreview>, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/theory/activities/{activityId}/corrections/preview"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("PreviewTheoryCorrection")); }
    public override async Task<Results<Ok<TheoryCorrectionPreview>, NotFound, ProblemHttpResult>> ExecuteAsync(PreviewTheoryCorrectionRequest request, CancellationToken ct)
        {
        var result = await store.PreviewAsync(user.UserId, request.ActivityId, request.Input, clock.GetUtcNow(), ct);
        return result.Preview is { } preview ? TypedResults.Ok(preview) : result.Failure == TheoryReviewFailure.NotFoundOrForbidden
            ? TypedResults.NotFound() : TheoryActivityHttp.Problem(result.Failure!);
    }
}
