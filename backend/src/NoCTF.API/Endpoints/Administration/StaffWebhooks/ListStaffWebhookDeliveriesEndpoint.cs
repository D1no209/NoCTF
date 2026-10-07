using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class ListStaffWebhookDeliveriesRequest : PaginationRequest { public Guid CompetitionId { get; set; } public Guid TargetId { get; set; } }
public sealed class ListStaffWebhookDeliveriesValidator : Validator<ListStaffWebhookDeliveriesRequest>
{ public ListStaffWebhookDeliveriesValidator() { PaginationRules.Add(this); RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty(); } }
public sealed class ListStaffWebhookDeliveriesEndpoint(IStaffWebhookStore store, IUserContext user)
    : Endpoint<ListStaffWebhookDeliveriesRequest, Results<Ok<StaffWebhookDeliveryPage>, ProblemHttpResult>>
{
    public override void Configure() { Get("/admin/competitions/{competitionId}/staff-webhooks/{targetId}/deliveries"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListStaffWebhookDeliveries"));
        Summary(value => { value.Summary = "ListStaffWebhookDeliveries for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Ok<StaffWebhookDeliveryPage>, ProblemHttpResult>> ExecuteAsync(ListStaffWebhookDeliveriesRequest request, CancellationToken ct)
    {
        var result = await store.DiagnosticsAsync(request.CompetitionId, request.TargetId, user.UserId, request.Offset, request.Limit, ct, request.Desc);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
