using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class SetTheoryQuestionStateRequest
{
    public Guid QuestionId { get; set; }
    public TheoryQuestionState State { get; set; }
}

public sealed class SetTheoryQuestionStateValidator : Validator<SetTheoryQuestionStateRequest>
{
    public SetTheoryQuestionStateValidator()
    {
        RuleFor(request => request.QuestionId).NotEmpty();
        RuleFor(request => request.State).IsInEnum();
    }
}

public sealed class SetTheoryQuestionStateEndpoint(ManageTheoryQuestions manage, IUserContext user, TimeProvider clock)
    : Endpoint<SetTheoryQuestionStateRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/theory/questions/{questionId}/state");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TheoryQuestionsSetState"));
        Summary(summary =>
        {
            summary.Summary = "Changes a theory question catalog state.";
            summary.Description = "Retiring a question prevents new selection without mutating published activity versions.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(SetTheoryQuestionStateRequest request, CancellationToken ct)
    {
        var failure = await manage.SetStateAsync(user.UserId, request.QuestionId, request.State, clock.GetUtcNow(), ct);
        return failure is null ? TypedResults.NoContent() : failure == TheoryBankFailure.NotFoundOrForbidden
            ? TypedResults.NotFound() : TheoryQuestionHttp.Problem(failure.Value);
    }
}
