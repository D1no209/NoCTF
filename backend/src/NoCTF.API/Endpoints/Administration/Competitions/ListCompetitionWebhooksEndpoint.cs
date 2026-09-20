using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http;
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
    InvalidCursor,
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

public sealed record CompetitionWebhookTargetListResponse(
    IReadOnlyList<CompetitionWebhookTargetResponse> Items,
    string? NextCursor,
    bool CanManage);

public sealed class ListCompetitionWebhooksRequest
{
    public Guid CompetitionId { get; set; }
    [QueryParam] public string? Cursor { get; set; }
    [QueryParam] public int Limit { get; set; } = 50;
}

public sealed class ListCompetitionWebhooksValidator
    : Validator<ListCompetitionWebhooksRequest>
{
    public ListCompetitionWebhooksValidator() =>
        RuleFor(request => request.Limit).InclusiveBetween(1, 100);
}

public sealed class ListCompetitionWebhooksEndpoint(
    ListCompetitionWebhookTargets list,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    SignedKeysetCursor cursors)
    : Endpoint<ListCompetitionWebhooksRequest,
        Results<Ok<CompetitionWebhookTargetListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>>
{
    private const string CursorEndpoint = "admin.competition.webhooks.list";

    public override void Configure()
    {
        Get("/admin/competitions/{competitionId}/webhooks");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionWebhooks"));
        Summary(summary => summary.Summary = "Lists outbound webhook targets for one competition.");
    }

    public override async Task<Results<Ok<CompetitionWebhookTargetListResponse>, NotFound, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        ListCompetitionWebhooksRequest request,
        CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var canManage = await authorizer.CanModerateAsync(user.UserId, request.CompetitionId, ct);
        var scope = request.CompetitionId.ToString("N");
        if (!cursors.TryDecode(request.Cursor, CursorEndpoint, scope, out var position))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid webhook cursor.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = CompetitionWebhookProblemCode.InvalidCursor
                });
        }
        var page = await list.ExecuteAsync(
            request.CompetitionId,
            position?.CreatedAt,
            position?.Id,
            request.Limit,
            ct);
        if (page is null)
            return TypedResults.NotFound();
        var items = page.Items.Select(item => CompetitionWebhookProtocol.ToResponse(item, canManage)).ToArray();
        var next = page.HasMore && items.Length > 0
            ? cursors.Encode(
                CursorEndpoint,
                scope,
                new(items[^1].UpdatedAt, items[^1].Id))
            : null;
        return TypedResults.Ok(new CompetitionWebhookTargetListResponse(items, next, canManage));
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
