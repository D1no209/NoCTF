using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;
using NoCTF.Domain.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class UpdateStaffWebhookRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TargetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public StaffWorkItemKindProtocol[] Categories { get; set; } = Enum.GetValues<StaffWorkItemKindProtocol>();
}
public sealed class UpdateStaffWebhookValidator : Validator<UpdateStaffWebhookRequest>
{
    public UpdateStaffWebhookValidator()
    {
        RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty();
        RuleFor(value => value.Name).NotEmpty().MaximumLength(100); RuleFor(value => value.EndpointUrl).NotEmpty().MaximumLength(2048);
        RuleFor(value => value.Categories).NotEmpty().Must(values => values is not null && values.Distinct().Count() == values.Length);
        RuleForEach(value => value.Categories).IsInEnum();
    }
}
public sealed class UpdateStaffWebhookEndpoint(ManageStaffWebhooks manage, IUserContext user)
    : Endpoint<UpdateStaffWebhookRequest, Results<Ok<StaffWebhookMutationResponse>, ProblemHttpResult>>
{
    public override void Configure() { Put("/admin/competitions/{competitionId}/staff-webhooks/{targetId}"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminUpdateStaffWebhook"));
        Summary(value => { value.Summary = "UpdateStaffWebhook for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Ok<StaffWebhookMutationResponse>, ProblemHttpResult>> ExecuteAsync(UpdateStaffWebhookRequest request, CancellationToken ct)
    {
        var result = await manage.SaveAsync(new(request.CompetitionId, user.UserId, request.TargetId, request.Name, request.EndpointUrl, request.Enabled,
            request.Categories.Select(value => (StaffWorkItemKind)(short)value).ToArray()), ct);
        return result.Succeeded ? TypedResults.Ok(StaffWebhookMutationResponse.From(result.Value!)) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
