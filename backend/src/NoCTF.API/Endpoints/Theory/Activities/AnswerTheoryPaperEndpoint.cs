using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class AnswerTheoryPaperRequest
{
    public Guid ActivityId { get; set; } public Guid PaperId { get; set; } public Guid ItemId { get; set; }
    public Guid ExpectedRevision { get; set; } public Guid RequestId { get; set; }
    public required Guid[] OptionIds { get; set; }
}
public sealed class AnswerTheoryPaperValidator : Validator<AnswerTheoryPaperRequest>
{
    public AnswerTheoryPaperValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.PaperId).NotEmpty();
        RuleFor(request => request.ItemId).NotEmpty(); RuleFor(request => request.ExpectedRevision).NotEmpty();
        RuleFor(request => request.RequestId).NotEmpty(); RuleFor(request => request.OptionIds).NotNull();
        When(request => request.OptionIds is not null, () => RuleFor(request => request.OptionIds.Length).LessThanOrEqualTo(8));
    }
}

public sealed class AnswerTheoryPaperEndpoint(ITheoryPaperStore store, IUserContext user, TimeProvider clock) : Endpoint<AnswerTheoryPaperRequest, Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Put("/theory/activities/{activityId}/papers/{paperId}/answers/{itemId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("AnswerTheoryPaper"));
    }
    public override async Task<Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>> ExecuteAsync(AnswerTheoryPaperRequest request, CancellationToken ct)
        => TheoryActivityHttp.Paper(await store.AnswerAsync(new(user.UserId, request.ActivityId, request.PaperId, request.ItemId, request.ExpectedRevision, request.RequestId, request.OptionIds, clock.GetUtcNow()), ct));
}
