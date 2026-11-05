using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Application.Questionnaires.Sessions;
using NoCTF.Domain.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class SaveQuestionnaireActivityRequest
{
    public Guid? ActivityId { get; set; }
    public required QuestionnaireActivityInput Input { get; set; }
}
public sealed class SaveQuestionnaireActivityValidator : Validator<SaveQuestionnaireActivityRequest>
{
    public SaveQuestionnaireActivityValidator()
    {
        RuleFor(request => request.Input).NotNull();
        When(request => request.Input is not null, () => RuleFor(request => request.Input).SetValidator(new QuestionnaireActivityInputValidator()));
    }
}
public sealed class QuestionnaireActivityInputValidator : AbstractValidator<QuestionnaireActivityInput>
{
    public QuestionnaireActivityInputValidator()
    {
        RuleFor(input => input.Name).NotEmpty().MaximumLength(160);
        RuleFor(input => input.Kind).IsInEnum(); RuleFor(input => input.ScoringMode).IsInEnum();
        RuleFor(input => input.DurationSeconds).GreaterThan(0); RuleFor(input => input.Rules).NotNull();
        RuleFor(input => input.SelectionKind).IsInEnum(); RuleFor(input => input.FixedQuestionnaires).NotNull();
        When(input => input.SelectionKind == QuestionnaireSelectionKind.Draw, () => RuleFor(input => input.Rules).NotEmpty());
        When(input => input.SelectionKind == QuestionnaireSelectionKind.Fixed, () => RuleFor(input => input.FixedQuestionnaires).NotEmpty());
        When(input => input.FixedQuestionnaires is not null, () => RuleForEach(input => input.FixedQuestionnaires).NotNull().ChildRules(rule =>
        { rule.RuleFor(value => value.ChallengeId).NotEmpty(); rule.RuleFor(value => value.PointsPerQuestion).GreaterThan(0); }));
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

public sealed class SaveQuestionnaireActivityEndpoint(IQuestionnaireActivityStore store, IUserContext user, TimeProvider clock)
    : Endpoint<SaveQuestionnaireActivityRequest, Results<Ok<QuestionnaireActivitySummary>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/questionnaires/activities"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("SaveQuestionnaireActivity"));
    }
    public override async Task<Results<Ok<QuestionnaireActivitySummary>, NotFound, ProblemHttpResult>> ExecuteAsync(SaveQuestionnaireActivityRequest request, CancellationToken ct)
        => QuestionnaireActivityHttp.Result(await store.SaveAsync(new(user.UserId, request.ActivityId, request.Input, clock.GetUtcNow()), ct));
}

internal static class QuestionnaireActivityHttp
{
    internal static Results<Ok<QuestionnaireActivitySummary>, NotFound, ProblemHttpResult> Result(QuestionnaireActivityResult result)
        => result.Activity is { } activity ? TypedResults.Ok(activity)
            : result.Failure == QuestionnaireActivityError.NotFoundOrForbidden ? TypedResults.NotFound()
            : Problem((Enum?)result.Validation ?? result.Failure!, result.Shortages);
    internal static Results<Ok<QuestionnaireSessionView>, NotFound, ProblemHttpResult> Session(QuestionnaireSessionResult result)
        => result.View is { } view ? TypedResults.Ok(view)
            : result.Failure == QuestionnaireSessionFailure.NotFoundOrForbidden ? TypedResults.NotFound() : Problem(result.Failure!, result.Shortages);
    internal static ProblemHttpResult Problem(Enum code, object? shortages = null) => ApiProblems.Problem(
        statusCode: code is QuestionnaireActivityFailure
            || code is QuestionnaireActivityError.InvalidConfiguration or QuestionnaireSessionFailure.InvalidOptions or QuestionnaireSessionFailure.InvalidRequestIdentity
                or QuestionnaireReviewFailure.InvalidCorrection or QuestionnaireReviewFailure.ReasonRequired ? StatusCodes.Status400BadRequest
            : code is QuestionnaireSessionFailure.DependencyUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
        detail: ApiMessages.For(code), extensions: new Dictionary<string, object?> { ["code"] = code.ToString(), ["shortages"] = shortages });
}
