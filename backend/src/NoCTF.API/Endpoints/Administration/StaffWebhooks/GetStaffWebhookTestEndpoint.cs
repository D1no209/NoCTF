using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class GetStaffWebhookTestRequest { public Guid CompetitionId { get; set; } public Guid TargetId { get; set; } public Guid DeliveryId { get; set; } }
public sealed class GetStaffWebhookTestValidator : Validator<GetStaffWebhookTestRequest>
{ public GetStaffWebhookTestValidator() { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty(); RuleFor(value => value.DeliveryId).NotEmpty(); } }
public sealed class GetStaffWebhookTestEndpoint(IStaffWebhookStore store, IUserContext user)
    : Endpoint<GetStaffWebhookTestRequest, Results<Ok<StaffWebhookDeliveryView>, ProblemHttpResult>>
{
    public override void Configure() { Get("/admin/competitions/{competitionId}/staff-webhooks/{targetId}/test-deliveries/{deliveryId}"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminGetStaffWebhookTest"));
        Summary(value => { value.Summary = "GetStaffWebhookTest for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Ok<StaffWebhookDeliveryView>, ProblemHttpResult>> ExecuteAsync(GetStaffWebhookTestRequest request, CancellationToken ct)
    {
        var result = await store.TestStatusAsync(request.CompetitionId, request.TargetId, request.DeliveryId, user.UserId, ct);
        return result.Succeeded ? TypedResults.Ok(result.Value!) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
