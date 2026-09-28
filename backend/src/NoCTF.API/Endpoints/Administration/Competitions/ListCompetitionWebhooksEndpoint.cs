using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.API.Serialization;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;

namespace NoCTF.API.Endpoints.Administration.Competitions;

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionWebhookDisabledReasonProtocol>))]
public enum CompetitionWebhookDisabledReasonProtocol
{
    ReceiverGone
}

[JsonConverter(typeof(StrictPascalCaseEnumConverter<CompetitionWebhookProblemCode>))]
public enum CompetitionWebhookProblemCode
{
    DuplicateEndpoint,
    InvalidEndpoint,
    InvalidName
}

public sealed record CompetitionWebhookTargetResponse(
    Guid Id,
    string Name,
    string EndpointHost,
    string? EndpointUrl,
    bool Enabled,
    DateTimeOffset? EnabledAt,
    bool SecretConfigured,
    DateTimeOffset? PreviousSecretValidUntil,
    CompetitionWebhookDisabledReasonProtocol? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CompetitionWebhookTargetListResponse
    : ArrayResult<CompetitionWebhookTargetResponse>
{
    public CompetitionWebhookTargetListResponse() { }

    public CompetitionWebhookTargetListResponse(
        CompetitionWebhookTargetResponse[] items,
        int total,
        bool canManage) : base(items, total) => CanManage = canManage;

    public bool CanManage { get; set; }
}

public sealed class ListCompetitionWebhooksRequest : PaginationRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class ListCompetitionWebhooksValidator
    : Validator<ListCompetitionWebhooksRequest>
{
    public ListCompetitionWebhooksValidator() => PaginationRules.Add(this);
}

public sealed class ListCompetitionWebhooksEndpoint(
    ListCompetitionWebhookTargets list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user)
    : Endpoint<ListCompetitionWebhooksRequest,
        Results<Ok<CompetitionWebhookTargetListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/webhooks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionWebhooks"));
        Summary(summary =>
        {
            summary.Summary = "Lists outbound webhook targets for one competition.";
            summary.Description = "Returns an offset page of targets with endpoint details redacted for read-only staff.";
        });
    }

    public override async Task<Results<Ok<CompetitionWebhookTargetListResponse>, NotFound, ForbidHttpResult>> ExecuteAsync(
        ListCompetitionWebhooksRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var canManage = await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct);
        var page = await list.ExecuteAsync(
            request.CompetitionId,
            request.Offset,
            request.Limit,
            request.Desc,
            ct);
        if (page is null)
            return TypedResults.NotFound();
        var items = page.Items.Select(item => CompetitionWebhookProtocol.ToResponse(item, canManage)).ToArray();
        return TypedResults.Ok(new CompetitionWebhookTargetListResponse(
            items,
            page.Total,
            canManage));
    }
}

internal static class CompetitionWebhookProtocol
{
    public static CompetitionWebhookTargetResponse ToResponse(
        CompetitionWebhookTargetView target,
        bool includeEndpoint) => new(
        target.Id,
        target.Name,
        new Uri(target.EndpointUrl).DnsSafeHost,
        includeEndpoint ? target.EndpointUrl : null,
        target.Enabled,
        target.EnabledAt,
        target.SecretConfigured,
        target.PreviousSecretValidUntil,
        target.DisabledReason is null
            ? null
            : target.DisabledReason.Value switch
            {
                CompetitionWebhookDisabledReason.ReceiverGone =>
                    CompetitionWebhookDisabledReasonProtocol.ReceiverGone,
                _ => throw new ArgumentOutOfRangeException()
            },
        target.CreatedAt,
        target.UpdatedAt);

    public static CompetitionWebhookProblemCode ToProblemCode(
        CompetitionWebhookMutationFailure failure) => failure switch
        {
            CompetitionWebhookMutationFailure.DuplicateEndpoint =>
                CompetitionWebhookProblemCode.DuplicateEndpoint,
            CompetitionWebhookMutationFailure.InvalidEndpoint =>
                CompetitionWebhookProblemCode.InvalidEndpoint,
            CompetitionWebhookMutationFailure.InvalidName =>
                CompetitionWebhookProblemCode.InvalidName,
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure, null)
        };
}
