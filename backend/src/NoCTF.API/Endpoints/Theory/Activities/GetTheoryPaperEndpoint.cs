using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class GetTheoryPaperRequest
{
    public Guid ActivityId { get; set; }
    public Guid? PaperId { get; set; }
}

public sealed class GetTheoryPaperEndpoint(ITheoryPaperStore store, IUserContext user, TimeProvider clock) : Endpoint<GetTheoryPaperRequest, Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/theory/activities/{activityId}/papers/{paperId}");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("GetTheoryPaper"));
    }
    public override async Task<Results<Ok<TheoryPaperView>, NotFound, ProblemHttpResult>> ExecuteAsync(GetTheoryPaperRequest request, CancellationToken ct)
        => TheoryActivityHttp.Paper(await store.ReadAsync(new(user.UserId, request.ActivityId, request.PaperId, null, clock.GetUtcNow()), ct));
}
