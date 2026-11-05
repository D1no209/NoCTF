using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class ListQuestionnaireReviewsRequest : NoCTF.API.Pagination.PaginationRequest { public Guid ActivityId { get; set; } }
public sealed class ListQuestionnaireReviewsValidator : Validator<ListQuestionnaireReviewsRequest>
{ public ListQuestionnaireReviewsValidator() { NoCTF.API.Pagination.PaginationRules.Add(this); RuleFor(request => request.ActivityId).NotEmpty(); } }
public sealed class ListQuestionnaireReviewsEndpoint(IQuestionnaireReviewStore store, IUserContext user, TimeProvider clock)
    : Endpoint<ListQuestionnaireReviewsRequest, Results<Ok<QuestionnaireReviewPage>, NotFound>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities/{activityId}/reviews"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse())); Description(builder => builder.WithName("ListQuestionnaireReviews"));
    }
    public override async Task<Results<Ok<QuestionnaireReviewPage>, NotFound>> ExecuteAsync(ListQuestionnaireReviewsRequest request, CancellationToken ct)
        => await store.ListAsync(user.UserId, request.ActivityId, request.Offset, request.Limit, clock.GetUtcNow(), ct) is { } page ? TypedResults.Ok(page) : TypedResults.NotFound();
}
