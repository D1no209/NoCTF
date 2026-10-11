using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class IssueTheoryPaperRequest { public Guid ActivityId { get; set; } public Guid RequestId { get; set; } public int ExpectedIssuedCount { get; set; } }
public sealed class IssueTheoryPaperValidator : Validator<IssueTheoryPaperRequest>
{
    public IssueTheoryPaperValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.RequestId).NotEmpty(); RuleFor(request => request.ExpectedIssuedCount).GreaterThanOrEqualTo(0); }
}

public sealed class IssueTheoryPaperEndpoint(ITheoryPaperStore store, IUserContext user, TimeProvider clock) : Endpoint<IssueTheoryPaperRequest, Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/activities/{activityId}/papers");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("IssueTheoryPaper"));
    }
    public override async Task<Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>> ExecuteAsync(IssueTheoryPaperRequest request, CancellationToken ct)
        => TheoryActivityHttp.Paper(await store.IssueAsync(new(user.UserId, request.ActivityId, null, null, clock.GetUtcNow(), request.RequestId, request.ExpectedIssuedCount), ct));
}
