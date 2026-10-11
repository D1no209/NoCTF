using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class CreateTheoryQuestionRequest
{
    public required TheoryQuestionInput Content { get; set; }
}

public sealed class CreateTheoryQuestionValidator : Validator<CreateTheoryQuestionRequest>
{
    public CreateTheoryQuestionValidator()
    {
        RuleFor(request => request.Content).NotNull();
        When(request => request.Content is not null, () => RuleFor(request => request.Content).SetValidator(new TheoryQuestionContentValidator()));
    }
}

public sealed class TheoryQuestionContentValidator : AbstractValidator<TheoryQuestionInput>
{
    public TheoryQuestionContentValidator()
    {
        RuleFor(content => content.Title).NotEmpty().MaximumLength(160);
        RuleFor(content => content.Body).NotEmpty().MaximumLength(64000);
        RuleFor(content => content.Explanation).MaximumLength(64000);
        RuleFor(content => content.Kind).IsInEnum();
        RuleFor(content => content.Difficulty).IsInEnum();
        RuleFor(content => content.Options).NotNull();
        RuleFor(content => content.Classifications).NotNull();
        RuleFor(content => content.FileIds).NotNull();
        When(content => content.Options is not null, () => RuleForEach(content => content.Options).NotNull().ChildRules(option =>
        {
            option.RuleFor(value => value.Content).NotEmpty().MaximumLength(16000);
            option.RuleFor(value => value.TruthValue).IsInEnum();
        }));
        When(content => content.Classifications is not null, () => RuleForEach(content => content.Classifications).NotNull().ChildRules(item =>
        {
            item.RuleFor(value => value.Name).NotEmpty().MaximumLength(80);
            item.RuleFor(value => value.Kind).IsInEnum();
        }));
    }
}

public sealed class CreateTheoryQuestionEndpoint(ManageTheoryQuestions manage, IUserContext user, TimeProvider clock)
    : Endpoint<CreateTheoryQuestionRequest, Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/questions");
        MaxRequestBodySize(2 * 1024 * 1024);
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("TheoryQuestionsCreate"));
        Summary(summary =>
        {
            summary.Summary = "Creates an independent theory question.";
            summary.Description = "Only resource managers can create a question; the first immutable version starts as a draft.";
        });
    }

    public override async Task<Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>> ExecuteAsync(CreateTheoryQuestionRequest request, CancellationToken ct)
        => TheoryQuestionHttp.Result(await manage.SaveAsync(new(user.UserId, null, request.Content, clock.GetUtcNow()), ct));
}

internal static class TheoryQuestionHttp
{
    internal static Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult> Result(TheoryBankWriteResult result)
        => result.Saved is { } saved ? TypedResults.Ok(saved)
            : result.Failure == TheoryBankFailure.NotFoundOrForbidden ? TypedResults.NotFound()
            : Problem(result.Failure ?? throw new InvalidOperationException("Missing theory write outcome."), result.Validation);

    internal static ProblemHttpResult Problem(TheoryBankFailure failure, TheoryQuestionFailure? validation = null)
        => ApiProblems.Problem(statusCode: failure switch
        {
            TheoryBankFailure.CreationForbidden or TheoryBankFailure.FileNotAuthorized => StatusCodes.Status403Forbidden,
            TheoryBankFailure.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, detail: ApiMessages.For((Enum?)validation ?? failure), extensions: new Dictionary<string, object?>
        { ["code"] = failure.ToString(), ["validationCode"] = validation?.ToString() });
}
