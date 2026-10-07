using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class RotateStaffWebhookSecretRequest { public Guid CompetitionId { get; set; } public Guid TargetId { get; set; } }
public sealed class RotateStaffWebhookSecretValidator : Validator<RotateStaffWebhookSecretRequest>
{ public RotateStaffWebhookSecretValidator() { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty(); } }
public sealed class RotateStaffWebhookSecretEndpoint(ManageStaffWebhooks manage, IUserContext user)
    : Endpoint<RotateStaffWebhookSecretRequest, Results<Ok<StaffWebhookMutationResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/staff-webhooks/{targetId}/rotate-secret"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminRotateStaffWebhookSecret"));
        Summary(value => { value.Summary = "RotateStaffWebhookSecret for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Ok<StaffWebhookMutationResponse>, ProblemHttpResult>> ExecuteAsync(RotateStaffWebhookSecretRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await manage.RotateAsync(request.CompetitionId, request.TargetId, user.UserId, ct);
        return result.Succeeded ? TypedResults.Ok(StaffWebhookMutationResponse.From(result.Value!)) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
