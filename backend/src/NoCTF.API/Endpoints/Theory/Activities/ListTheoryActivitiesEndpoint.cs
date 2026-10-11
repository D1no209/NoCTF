using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Theory.Activities;
using NoCTF.Application.Theory.Papers;

namespace NoCTF.API.Endpoints.Theory.Activities;

public sealed class ListTheoryActivitiesRequest : NoCTF.API.Pagination.SearchRequest
{
    [QueryParam] public bool Staff { get; set; }
    [QueryParam] public Guid? CompetitionId { get; set; }
}
public sealed class ListTheoryActivitiesValidator : Validator<ListTheoryActivitiesRequest>
{ public ListTheoryActivitiesValidator() => NoCTF.API.Pagination.PaginationRules.AddSearch(this); }

public sealed class ListTheoryActivitiesEndpoint(ITheoryActivityStore store, IUserContext user) : Endpoint<ListTheoryActivitiesRequest, Ok<TheoryActivityPage>>
{
    public override void Configure()
    {
        Get("/theory/activities");
        AuthSchemes("Bearer");
        Options(builder => builder.WithMetadata(new NoCTF.API.Endpoints.Theory.TheoryPrivateResponse()));
        MaxRequestBodySize(1024 * 1024);
        Description(builder => builder.WithName("ListTheoryActivities"));
    }
    public override async Task<Ok<TheoryActivityPage>> ExecuteAsync(ListTheoryActivitiesRequest request, CancellationToken ct)
        => TypedResults.Ok(await store.ListAsync(user.UserId, request.Staff, request.CompetitionId, ct, request.Offset, request.Limit, request.Keyword));
}
