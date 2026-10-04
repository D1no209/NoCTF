using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using NoCTF.API.Pagination;
using NoCTF.API.Security;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Infrastructure.Competitions.Webhooks;

namespace NoCTF.API.Endpoints.Administration.Competitions;

public sealed class ListCompetitionWebhookDeliveriesRequest : PaginationRequest
{
    public Guid CompetitionId { get; set; }
}

public sealed class ListCompetitionWebhookDeliveriesValidator
    : Validator<ListCompetitionWebhookDeliveriesRequest>
{
    public ListCompetitionWebhookDeliveriesValidator() => PaginationRules.Add(this);
}

public sealed record CompetitionWebhookDeliveryDiagnosticResponse(
    Guid EventId,
    Guid TargetId,
    string EventType,
    string State,
    string PayloadState,
    long EventSequence,
    Guid CompetitionRevision,
    DateTimeOffset DomainEventCreatedAt,
    DateTimeOffset OutboxPersistedAt,
    DateTimeOffset? WorkerDequeuedAt,
    DateTimeOffset? PublicProjectionReadyAt,
    DateTimeOffset? CapturedAt,
    double QueueAgeSeconds,
    double? ProjectionWaitSeconds,
    DateTimeOffset? FirstHttpAttemptStartedAt,
    DateTimeOffset? LastHttpAttemptStartedAt,
    DateTimeOffset? LastHttpAttemptCompletedAt,
    double? LastHttpAttemptDurationSeconds,
    int? LastHttpStatusCode,
    int ProjectionRetryCount,
    int HttpRetryCount,
    DateTimeOffset? NextRetryAt,
    string? DeadLetterReason);

public sealed class CompetitionWebhookDeliveryListResponse
    : ArrayResult<CompetitionWebhookDeliveryDiagnosticResponse>
{
    public CompetitionWebhookDeliveryListResponse() { }

    public CompetitionWebhookDeliveryListResponse(
        CompetitionWebhookDeliveryDiagnosticResponse[] items, int total)
        : base(items, total) { }
}

public sealed class ListCompetitionWebhookDeliveriesEndpoint(
    ICompetitionWebhookDeliveryStore deliveries,
    ICompetitionModerationAuthorizer authorizer,
    IUserContext user,
    TimeProvider clock)
    : Endpoint<ListCompetitionWebhookDeliveriesRequest,
        Results<Ok<CompetitionWebhookDeliveryListResponse>, NotFound, ForbidHttpResult>>
{
    public override void Configure()
    {
        Summary(summary =>
        {
            summary.Summary = "Lists delivery attempts and results for competition webhooks.";
            summary.Description = summary.Summary;
        });

        Get("/admin/competitions/{competitionId}/webhook-deliveries");
        AuthSchemes("Bearer");
        Description(builder => builder.WithName("AdminListCompetitionWebhookDeliveries"));
    }

    public override async Task<Results<Ok<CompetitionWebhookDeliveryListResponse>,
        NotFound, ForbidHttpResult>> ExecuteAsync(
        ListCompetitionWebhookDeliveriesRequest request, CancellationToken ct)
    {
        if (!await authorizer.CanObserveAsync(user.UserId, request.CompetitionId, ct))
            return TypedResults.Forbid();
        var page = await deliveries.ListDiagnosticsAsync(
            request.CompetitionId, request.Offset, request.Limit, request.Desc, ct);
        if (page is null)
            return TypedResults.NotFound();
        var now = clock.GetUtcNow();
        return TypedResults.Ok(new CompetitionWebhookDeliveryListResponse(
            page.Items.Select(item => new CompetitionWebhookDeliveryDiagnosticResponse(
                item.EventId, item.TargetId,
                CompetitionWebhookEventTypes.From(item.EventKind) ?? item.EventKind.ToString(),
                item.State.ToString(), item.PayloadState.ToString(),
                item.EventSequence, item.CompetitionRevision,
                item.DomainEventCreatedAt, item.OutboxPersistedAt,
                item.WorkerDequeuedAt, item.PublicProjectionReadyAt,
                item.CapturedAt,
                Math.Max(0, ((item.FirstHttpAttemptStartedAt ?? now)
                    - item.DomainEventCreatedAt).TotalSeconds),
                item.WorkerDequeuedAt is { } dequeuedAt
                    ? Math.Max(0, ((item.PublicProjectionReadyAt ?? now)
                        - dequeuedAt).TotalSeconds)
                    : null,
                item.FirstHttpAttemptStartedAt,
                item.LastHttpAttemptStartedAt,
                item.LastHttpAttemptCompletedAt,
                item.LastHttpAttemptStartedAt is { } attemptedAt
                    && item.LastHttpAttemptCompletedAt is { } completedAt
                    ? Math.Max(0, (completedAt - attemptedAt).TotalSeconds)
                    : null,
                item.LastHttpStatusCode, item.ProjectionRetryCount,
                item.HttpRetryCount, item.NextRetryAt,
                item.DeadLetterReason?.ToString()))
                .ToArray(), page.Total));
    }
}
