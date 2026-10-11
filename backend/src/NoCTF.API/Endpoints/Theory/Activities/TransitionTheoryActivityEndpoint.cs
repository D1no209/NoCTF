using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class TransitionTheoryActivityRequest
{
    public Guid ActivityId { get; set; }
    public TheoryActivityAction Action { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? Reason { get; set; }
}
public sealed class TransitionTheoryActivityValidator : Validator<TransitionTheoryActivityRequest>
{
    public TransitionTheoryActivityValidator()
    { RuleFor(request => request.ActivityId).NotEmpty(); RuleFor(request => request.Action).IsInEnum(); RuleFor(request => request.Reason).MaximumLength(2000); }
}

public sealed class TransitionTheoryActivityEndpoint(ITheoryActivityStore store, IUserContext user, TimeProvider clock) : Endpoint<TransitionTheoryActivityRequest, Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/theory/activities/{activityId}/operations");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("TransitionTheoryActivity"));
    }
    public override async Task<Results<Ok<TheoryActivitySummary>, NotFound, ProblemHttpResult>> ExecuteAsync(TransitionTheoryActivityRequest request, CancellationToken ct)
        => TheoryActivityHttp.Result(await store.TransitionAsync(new(user.UserId, request.ActivityId, request.Action, clock.GetUtcNow(), request.Deadline, request.Reason), ct));
}
