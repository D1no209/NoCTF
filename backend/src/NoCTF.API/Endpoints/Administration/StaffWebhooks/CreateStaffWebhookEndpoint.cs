using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.StaffWebhooks;
using NoCTF.Domain.Competitions.StaffWebhooks;

namespace NoCTF.API.Endpoints.Administration.StaffWebhooks;

[System.Text.Json.Serialization.JsonConverter(typeof(NoCTF.API.Serialization.StrictPascalCaseEnumConverter<StaffWorkItemKindProtocol>))]
public enum StaffWorkItemKindProtocol : short { CheatIncident, Consultation, BanAppeal }
public sealed record StaffWebhookTargetResponse(Guid Id, string Name, string? EndpointUrl, bool Enabled, bool AuthorizationRevoked,
    IReadOnlyList<StaffWorkItemKindProtocol> Categories, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? PreviousSecretValidUntil, bool Synchronizing, DateTimeOffset? FailureSince)
{
    public static StaffWebhookTargetResponse From(StaffWebhookTargetView value) => new(value.Id, value.Name, value.EndpointUrl,
        value.Enabled, value.AuthorizationRevoked, value.Categories.Select(kind => (StaffWorkItemKindProtocol)(short)kind).ToArray(),
        value.CreatedAt, value.UpdatedAt, value.PreviousSecretValidUntil, value.Synchronizing, value.FailureSince);
}
public sealed record StaffWebhookMutationResponse(StaffWebhookTargetResponse Target, string? SigningSecret)
{
    public static StaffWebhookMutationResponse From(StaffWebhookMutation value) => new(StaffWebhookTargetResponse.From(value.Target), value.SigningSecret);
}

public sealed class CreateStaffWebhookRequest
{
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public StaffWorkItemKindProtocol[] Categories { get; set; } = Enum.GetValues<StaffWorkItemKindProtocol>();
}
public sealed class CreateStaffWebhookValidator : Validator<CreateStaffWebhookRequest>
{
    public CreateStaffWebhookValidator()
    {
        RuleFor(value => value.CompetitionId).NotEmpty();
        RuleFor(value => value.Name).NotEmpty().MaximumLength(100);
        RuleFor(value => value.EndpointUrl).NotEmpty().MaximumLength(2048);
        RuleFor(value => value.Categories).NotEmpty().Must(values => values is not null && values.Distinct().Count() == values.Length);
        RuleForEach(value => value.Categories).IsInEnum();
    }
}
public sealed class CreateStaffWebhookEndpoint(ManageStaffWebhooks manage, IUserContext user)
    : Endpoint<CreateStaffWebhookRequest, Results<Created<StaffWebhookMutationResponse>, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/admin/competitions/{competitionId}/staff-webhooks"); AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminCreateStaffWebhook"));
        Summary(value => { value.Summary = "Creates a staff-only outbound webhook subscription.";
            value.Description = "Explicitly authorizes sharing the selected staff summaries with the external receiver. Only platform administrators, competition owners and managers may create subscriptions."; });
    }
    public override async Task<Results<Created<StaffWebhookMutationResponse>, ProblemHttpResult>> ExecuteAsync(CreateStaffWebhookRequest request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "private, no-store";
        var result = await manage.SaveAsync(new(request.CompetitionId, user.UserId, null, request.Name, request.EndpointUrl, request.Enabled,
            request.Categories.Select(value => (StaffWorkItemKind)(short)value).ToArray()), ct);
        return result.Succeeded ? TypedResults.Created($"/api/v1/admin/competitions/{request.CompetitionId}/staff-webhooks/{result.Value!.Target.Id}", StaffWebhookMutationResponse.From(result.Value))
            : StaffWebhookResults.Failure(result.FailureCode!.Value);
    }
}
internal static class StaffWebhookResults
{
    public static ProblemHttpResult Failure(StaffWebhookFailure code) => TypedResults.Problem(statusCode: code switch
    { StaffWebhookFailure.Forbidden => 403, StaffWebhookFailure.NotFound => 404,
        StaffWebhookFailure.Conflict or StaffWebhookFailure.DuplicateEndpoint => 409, _ => 400 },
        title: "Staff webhook operation failed.", extensions: new Dictionary<string, object?>
        { ["code"] = "StaffWebhook" + code, ["messageKey"] = "staffWebhook.errors." + char.ToLowerInvariant(code.ToString()[0]) + code.ToString()[1..] });
}
