using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Questions;
using NoCTF.Domain.Theory.Questions;

namespace NoCTF.API.Endpoints.Theory.Questions;

public sealed class GrantTheoryQuestionRequest
{
    public Guid QuestionId { get; set; }
    public Guid UserId { get; set; }
    public TheoryQuestionPermission? Permission { get; set; }
}

public sealed class GrantTheoryQuestionValidator : Validator<GrantTheoryQuestionRequest>
{
    public GrantTheoryQuestionValidator()
    {
        RuleFor(request => request.QuestionId).NotEmpty();
        RuleFor(request => request.UserId).NotEmpty();
        RuleFor(request => request.Permission).IsInEnum();
    }
}

public sealed class GrantTheoryQuestionEndpoint(ManageTheoryQuestions manage, IUserContext user)
    : Endpoint<GrantTheoryQuestionRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/theory/questions/{questionId}/grants/{userId}");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("TheoryQuestionsGrant"));
        Summary(summary =>
        {
            summary.Summary = "Changes a theory question collaborator grant.";
            summary.Description = "Requires management permission; an explicit null permission revokes the target grant.";
        });
    }

    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(GrantTheoryQuestionRequest request, CancellationToken ct)
    {
        var failure = await manage.GrantAsync(user.UserId, request.QuestionId, request.UserId, request.Permission, ct);
        return failure is null ? TypedResults.NoContent() : failure == TheoryBankFailure.NotFoundOrForbidden
            ? TypedResults.NotFound() : TheoryQuestionHttp.Problem(failure.Value);
    }
}
