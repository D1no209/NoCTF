using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class GetTheoryQuestionRequest
{
    public Guid QuestionId { get; set; }
}

public sealed class GetTheoryQuestionValidator : Validator<GetTheoryQuestionRequest>
{
    public GetTheoryQuestionValidator() => RuleFor(request => request.QuestionId).NotEmpty();
}

public sealed class GetTheoryQuestionEndpoint(ITheoryQuestionBank bank, IUserContext user)
    : Endpoint<GetTheoryQuestionRequest, Results<Ok<TheoryQuestionContent>, NotFound>>
{
    public override void Configure()
    {
        Get("/theory/questions/{questionId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TheoryQuestionsGet"));
        Summary(summary =>
        {
            summary.Summary = "Reads theory question content for an editor.";
            summary.Description = "Requires editing permission; use-only grantees cannot retrieve answer content.";
        });
    }

    public override async Task<Results<Ok<TheoryQuestionContent>, NotFound>> ExecuteAsync(GetTheoryQuestionRequest request, CancellationToken ct)
        => await bank.ReadAsync(user.UserId, request.QuestionId, ct) is { } content ? TypedResults.Ok(content) : TypedResults.NotFound();
}
