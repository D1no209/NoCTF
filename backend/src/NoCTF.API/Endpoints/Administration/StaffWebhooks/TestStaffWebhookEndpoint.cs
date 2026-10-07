using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

public sealed class TestStaffWebhookRequest { public Guid CompetitionId { get; set; } public Guid TargetId { get; set; } }
public sealed class TestStaffWebhookValidator : Validator<TestStaffWebhookRequest>
{ public TestStaffWebhookValidator() { RuleFor(value => value.CompetitionId).NotEmpty(); RuleFor(value => value.TargetId).NotEmpty(); } }
public sealed record StaffWebhookTestResponse(Guid DeliveryId, string StatusUrl);
public sealed class TestStaffWebhookEndpoint(ManageStaffWebhooks manage, IUserContext user)
    : Endpoint<TestStaffWebhookRequest, Results<Accepted<StaffWebhookTestResponse>, ProblemHttpResult>>
{
    public override void Configure() { Post("/admin/competitions/{competitionId}/staff-webhooks/{targetId}/test-deliveries"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminTestStaffWebhook"));
        Summary(value => { value.Summary = "TestStaffWebhook for a staff-only subscription."; value.Description = "Requires current competition staff authorization. Mutations are limited to platform administrators, owners and managers; read-only staff receive redacted subscription metadata."; }); }
    public override async Task<Results<Accepted<StaffWebhookTestResponse>, ProblemHttpResult>> ExecuteAsync(TestStaffWebhookRequest request, CancellationToken ct)
    {
        var result = await manage.TestAsync(request.CompetitionId, request.TargetId, user.UserId, ct);
        var url = $"/api/v1/admin/competitions/{request.CompetitionId}/staff-webhooks/{request.TargetId}/test-deliveries/{result.Value}";
        return result.Succeeded ? TypedResults.Accepted(url, new StaffWebhookTestResponse(result.Value, url)) : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
