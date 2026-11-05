using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Questionnaires.Activities;

namespace NoCTF.API.Endpoints.Questionnaires.Activities;

public sealed class ListQuestionnaireActivitiesRequest : NoCTF.API.Pagination.SearchRequest { [QueryParam] public bool Staff { get; set; } }
public sealed class ListQuestionnaireActivitiesValidator : Validator<ListQuestionnaireActivitiesRequest>
{ public ListQuestionnaireActivitiesValidator() => NoCTF.API.Pagination.PaginationRules.AddSearch(this); }
public sealed class ListQuestionnaireActivitiesEndpoint(IQuestionnaireActivityStore store, IUserContext user)
    : Endpoint<ListQuestionnaireActivitiesRequest, Ok<QuestionnaireActivityPage>>
{
    public override void Configure()
    {
        Get("/questionnaires/activities"); AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new PrivateEndpointResponse()));
        Description(builder => builder.WithName("ListQuestionnaireActivities"));
    }
    public override async Task<Ok<QuestionnaireActivityPage>> ExecuteAsync(ListQuestionnaireActivitiesRequest request, CancellationToken ct)
        => TypedResults.Ok(await store.ListAsync(user.UserId, request.Staff, ct, request.Offset, request.Limit, request.Keyword));
}
