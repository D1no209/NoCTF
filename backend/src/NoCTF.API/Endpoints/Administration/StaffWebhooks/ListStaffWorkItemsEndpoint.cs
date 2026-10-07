using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class ListStaffWorkItemsRequest : PaginationRequest { public Guid CompetitionId { get; set; } public bool PendingOnly { get; set; } = true; }
public sealed class ListStaffWorkItemsValidator : Validator<ListStaffWorkItemsRequest>
{ public ListStaffWorkItemsValidator() { PaginationRules.Add(this); RuleFor(value => value.CompetitionId).NotEmpty(); } }
public sealed class ListStaffWorkItemsEndpoint(IStaffWebhookStore store, IUserContext user)
    : Endpoint<ListStaffWorkItemsRequest, Results<Ok<StaffWorkItemPage>, ProblemHttpResult>>
{
    public override void Configure() { Get("/competitions/{competitionId}/staff-work-items"); AuthSchemes("Bearer"); }
    public override async Task<Results<Ok<StaffWorkItemPage>, ProblemHttpResult>> ExecuteAsync(ListStaffWorkItemsRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.WorkItemsAsync(request.CompetitionId, user.UserId, request.Offset, request.Limit, request.PendingOnly, ct, request.Desc);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
