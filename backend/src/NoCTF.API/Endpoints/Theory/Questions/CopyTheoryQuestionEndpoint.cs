using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class CopyTheoryQuestionRequest
{
    public Guid QuestionId { get; set; }
}

public sealed class CopyTheoryQuestionValidator : Validator<CopyTheoryQuestionRequest>
{
    public CopyTheoryQuestionValidator() => RuleFor(request => request.QuestionId).NotEmpty();
}

public sealed class CopyTheoryQuestionEndpoint(ITheoryQuestionBank bank, IUserContext user, TimeProvider clock)
    : Endpoint<CopyTheoryQuestionRequest, Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/questions/{questionId}/copy");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TheoryQuestionsCopy"));
        Summary(summary =>
        {
            summary.Summary = "Copies a theory question into an independent draft.";
            summary.Description = "Requires source content access and resource creation permission; preserves canonical source identity.";
        });
    }

    public override async Task<Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>> ExecuteAsync(CopyTheoryQuestionRequest request, CancellationToken ct)
        => TheoryQuestionHttp.Result(await bank.CopyAsync(user.UserId, request.QuestionId, clock.GetUtcNow(), ct));
}
