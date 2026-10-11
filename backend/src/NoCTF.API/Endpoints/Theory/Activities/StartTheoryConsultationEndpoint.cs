using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class StartTheoryConsultationRequest
{
    public Guid ActivityId { get; set; }
    public Guid PaperId { get; set; }
    public required string Body { get; set; }
}
public sealed record TheoryConsultationResponse(Guid ThreadRootId);
public sealed class StartTheoryConsultationValidator : Validator<StartTheoryConsultationRequest>
{
    public StartTheoryConsultationValidator()
    {
        RuleFor(request => request.ActivityId).NotEmpty();
        RuleFor(request => request.PaperId).NotEmpty();
        RuleFor(request => request.Body).NotEmpty().MaximumLength(2000);
    }
}
public sealed class StartTheoryConsultationEndpoint(ITheoryReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<StartTheoryConsultationRequest, Results<Ok<TheoryConsultationResponse>, NotFound>>
{
    public override void Configure()
    {
        Post("/theory/activities/{activityId}/reviews/{paperId}/consultations"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(16384); Description(builder => builder.WithName("StartTheoryConsultation"));
    }
    public override async Task<Results<Ok<TheoryConsultationResponse>, NotFound>> ExecuteAsync(StartTheoryConsultationRequest request, CancellationToken ct)
        => await store.StartConsultationAsync(user.UserId, request.ActivityId, request.PaperId, request.Body, clock.GetUtcNow(), ct) is { } id
            ? TypedResults.Ok(new TheoryConsultationResponse(id)) : TypedResults.NotFound();
}
