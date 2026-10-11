using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class SubmitTheoryPaperRequest
{
    public Guid ActivityId { get; set; } public Guid PaperId { get; set; } public Guid ExpectedRevision { get; set; }
}
public sealed class SubmitTheoryPaperValidator : Validator<SubmitTheoryPaperRequest>
{
    public SubmitTheoryPaperValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.PaperId).NotEmpty(); RuleFor(request => request.ExpectedRevision).NotEmpty(); }
}

public sealed class SubmitTheoryPaperEndpoint(ITheoryPaperStore store, IUserContext user, TimeProvider clock) : Endpoint<SubmitTheoryPaperRequest, Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/activities/{activityId}/papers/{paperId}/submit");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("SubmitTheoryPaper"));
    }
    public override async Task<Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>> ExecuteAsync(SubmitTheoryPaperRequest request, CancellationToken ct)
        => TheoryActivityHttp.Paper(await store.SubmitAsync(new(user.UserId, request.ActivityId, request.PaperId, request.ExpectedRevision, clock.GetUtcNow()), ct));
}
