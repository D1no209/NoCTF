using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class ListTheoryQuestionsRequest : SearchRequest
{
    [QueryParam] public TheoryQuestionKind? Kind { get; set; }
}

public sealed class ListTheoryQuestionsValidator : Validator<ListTheoryQuestionsRequest>
{
    public ListTheoryQuestionsValidator()
    {
        PaginationRules.AddSearch(this);
        RuleFor(request => request.Kind).IsInEnum();
    }
}

public sealed class ListTheoryQuestionsEndpoint(ITheoryQuestionBank bank, IUserContext user)
    : Endpoint<ListTheoryQuestionsRequest, Ok<TheoryQuestionPage>>
{
    public override void Configure()
    {
        Get("/theory/questions");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TheoryQuestionsList"));
        Summary(summary =>
        {
            summary.Summary = "Lists authorized theory question metadata.";
            summary.Description = "Use-only grants expose summaries without question bodies, answer keys or explanations.";
        });
    }

    public override async Task<Ok<TheoryQuestionPage>> ExecuteAsync(ListTheoryQuestionsRequest request, CancellationToken ct)
        => TypedResults.Ok(await bank.ListAsync(user.UserId, request.Keyword, request.Kind, request.Offset, request.Limit, ct));
}
