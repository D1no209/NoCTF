using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class SaveTheoryActivityRequest
{
    public Guid? ActivityId { get; set; }
    public Guid? CompetitionId { get; set; }
    public required TheoryActivityInput Input { get; set; }
}
public sealed class SaveTheoryActivityValidator : Validator<SaveTheoryActivityRequest>
{
    public SaveTheoryActivityValidator()
    {
        RuleFor(request => request.Input).NotNull();
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new TheoryActivityInputValidator()));
    }
}
public sealed class TheoryActivityInputValidator : AbstractValidator<TheoryActivityInput>
{
    public TheoryActivityInputValidator()
    {
        RuleFor(input => input.Name).NotEmpty().MaximumLength(160);
        RuleFor(input => input.Kind).IsInEnum(); RuleFor(input => input.ScoringMode).IsInEnum();
        RuleFor(input => input.DurationSeconds).GreaterThan(0); RuleFor(input => input.Rules).NotEmpty();
        When(input => input.Rules is not null, () => RuleForEach(input => input.Rules).NotNull().ChildRules(rule =>
        {
            rule.RuleFor(value => value.Kind).IsInEnum(); rule.RuleFor(value => value.Difficulty).IsInEnum();
            rule.RuleFor(value => value.Count).GreaterThan(0); rule.RuleFor(value => value.Points).GreaterThan(0);
            rule.RuleFor(value => value.Filters).NotNull();
            rule.RuleForEach(value => value.Filters).NotNull().ChildRules(filter =>
            { filter.RuleFor(value => value.Name).NotEmpty().MaximumLength(80); filter.RuleFor(value => value.Kind).IsInEnum(); });
        }));
    }
}

public sealed class SaveTheoryActivityEndpoint(ITheoryActivityStore store, IUserContext user, TimeProvider clock) : Endpoint<SaveTheoryActivityRequest, Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/activities");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("SaveTheoryActivity"));
    }
    public override async Task<Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>> ExecuteAsync(SaveTheoryActivityRequest request, CancellationToken ct)
        => TheoryActivityHttp.Result(await store.SaveAsync(new(user.UserId, request.ActivityId, request.CompetitionId, request.Input, clock.GetUtcNow()), ct));
}

internal static class TheoryActivityHttp
{
    internal static Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult> Result(TheoryActivityResult result)
        => result.Activity is { } activity ? TypedResults.Ok(activity)
            : result.Failure == TheoryActivityError.NotFoundOrForbidden ? TypedResults.NotFound()
            : Problem((Enum?)result.Validation ?? result.Failure!, result.Shortages);
    internal static Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult> Paper(TheoryPaperResult result)
        => result.View is { } view ? TypedResults.Ok(view)
            : result.Failure == TheoryPaperFailure.NotFoundOrForbidden ? TypedResults.NotFound() : Problem(result.Failure!, result.Shortages);
    internal static ProblemHttpResult Problem(Enum code, object? shortages = null) => ApiProblems.Problem(
        statusCode: code is TheoryActivityFailure
            || code is TheoryActivityError.InvalidConfiguration or TheoryPaperFailure.InvalidOptions or TheoryPaperFailure.InvalidRequestIdentity
                or TheoryReviewFailure.InvalidCorrection or TheoryReviewFailure.ReasonRequired ? StatusCodes.Status400BadRequest
            : code is TheoryPaperFailure.DependencyUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
        detail: ApiMessages.For(code),
        extensions: new Dictionary<string, object?> { ["code"] = code.ToString(), ["shortages"] = shortages });
}
