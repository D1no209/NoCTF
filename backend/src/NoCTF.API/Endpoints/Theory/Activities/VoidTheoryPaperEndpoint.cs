using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class VoidTheoryPaperRequest { public Guid ActivityId { get; set; } public Guid PaperId { get; set; } public required string Reason { get; set; } }
public sealed class VoidTheoryPaperValidator : Validator<VoidTheoryPaperRequest>
{ public VoidTheoryPaperValidator() { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.PaperId).NotEmpty(); RuleFor(request => request.Reason).NotEmpty().MaximumLength(2000); } }

public sealed class VoidTheoryPaperEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock) : Endpoint<VoidTheoryPaperRequest, Results<NoContent, NotFound, ProblemHttpResult>>
{
    public override void Configure() { Post("/theory/activities/{activityId}/reviews/{paperId}/void"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
         MaxRequestBodySize(65536); Description(builder => builder.WithName("VoidTheoryPaper")); }
    public override async Task<Results<NoContent, NotFound, ProblemHttpResult>> ExecuteAsync(VoidTheoryPaperRequest request, CancellationToken ct)
        {
        var failure = await store.VoidPaperAsync(user.UserId, request.ActivityId, request.PaperId, request.Reason, clock.GetUtcNow(), ct);
        return failure is null ? TypedResults.NoContent() : failure == TheoryReviewFailure.NotFoundOrForbidden ? TypedResults.NotFound() : TheoryActivityHttp.Problem(failure);
    }
}
