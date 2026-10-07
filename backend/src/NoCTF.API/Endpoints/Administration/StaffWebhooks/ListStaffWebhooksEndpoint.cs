using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class ListStaffWebhooksRequest : PaginationRequest { public Guid CompetitionId { get; set; } }
public sealed record StaffWebhookTargetPageResponse(IReadOnlyList<StaffWebhookTargetResponse> Items, int Total, bool CanManage);
public sealed class ListStaffWebhooksValidator : Validator<ListStaffWebhooksRequest>
{ public ListStaffWebhooksValidator() { PaginationRules.Add(this); RuleFor(value => value.CompetitionId).NotEmpty(); } }
public sealed class ListStaffWebhooksEndpoint(IStaffWebhookStore store, IUserContext user)
    : Endpoint<ListStaffWebhooksRequest, Results<Ok<StaffWebhookTargetPageResponse>, ProblemHttpResult>>
{
    public override void Configure() { Get("/admin/competitions/{competitionId}/staff-webhooks"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListStaffWebhooks"));
        Summary(value => { value.Summary = "ListStaffWebhooks for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Ok<StaffWebhookTargetPageResponse>, ProblemHttpResult>> ExecuteAsync(ListStaffWebhooksRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await store.ListAsync(request.CompetitionId, user.UserId, request.Offset, request.Limit, ct, request.Desc);
        return result.Succeeded ? TypedResults.Ok(new StaffWebhookTargetPageResponse(result.Value!.Items.Select(StaffWebhookTargetResponse.From).ToArray(), result.Value.Total, result.Value.CanManage))
            : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
