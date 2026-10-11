using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class UpdateTheoryQuestionRequest
{
    public Guid QuestionId { get; set; }
    public required TheoryQuestionInput Content { get; set; }
}

public sealed class UpdateTheoryQuestionValidator : Validator<UpdateTheoryQuestionRequest>
{
    public UpdateTheoryQuestionValidator()
    {
        RuleFor(request => request.QuestionId).NotEmpty();
        RuleFor(request => request.Content).NotNull();
        When(request => request.Content is not null, () => RuleFor(request => request.Content).SetValidator(new TheoryQuestionContentValidator()));
    }
}

public sealed class UpdateTheoryQuestionEndpoint(ManageTheoryQuestions manage, IUserContext user, TimeProvider clock)
    : Endpoint<UpdateTheoryQuestionRequest, Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/theory/questions/{questionId}");
        MaxRequestBodySize(2 * 1024 * 1024);
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        Description(builder => builder.WithName("TheoryQuestionsUpdate"));
        Summary(summary =>
        {
            summary.Summary = "Creates a new theory question version.";
            summary.Description = "Requires editing permission and preserves all previously issued versions.";
        });
    }

    public override async Task<Results<Ok<TheoryQuestionSaved>, NotFound, ProblemHttpResult>> ExecuteAsync(UpdateTheoryQuestionRequest request, CancellationToken ct)
        => TheoryQuestionHttp.Result(await manage.SaveAsync(new(user.UserId, request.QuestionId, request.Content, clock.GetUtcNow()), ct));
}
