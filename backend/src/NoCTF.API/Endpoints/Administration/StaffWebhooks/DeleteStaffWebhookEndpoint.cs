using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class DeleteStaffWebhookRequest { public Guid CompetitionId { get; set; } public Guid TargetId { get; set; } }
public sealed class DeleteStaffWebhookValidator : Validator<DeleteStaffWebhookRequest>
{ public DeleteStaffWebhookValidator() { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty(); } }
public sealed class DeleteStaffWebhookEndpoint(ManageStaffWebhooks manage, IUserContext user)
    : Endpoint<DeleteStaffWebhookRequest, Results<NoContent, ProblemHttpResult>>
{
    public override void Configure() { Delete("/admin/competitions/{competitionId}/staff-webhooks/{targetId}"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminDeleteStaffWebhook"));
        Summary(value => { value.Summary = "DeleteStaffWebhook for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<NoContent, ProblemHttpResult>> ExecuteAsync(DeleteStaffWebhookRequest request, CancellationToken ct)
    {
        var failure = await manage.DeleteAsync(request.CompetitionId, request.TargetId, user.UserId, ct);
        return failure is null ? TypedResults.NoContent() : StaffWebhookResults.Failure(failure.Value);
    }
}
