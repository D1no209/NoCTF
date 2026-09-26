using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Webhooks;

namespace NoCTF.Infrastructure.Competitions.Webhooks;

[Index(nameof(EventId), IsUnique = true)]
[Index(nameof(CompetitionId), nameof(NextDispatchAt), nameof(Sequence))]
public sealed class CompetitionWebhookOutboxEvent
{
    [Key]
    public long Sequence { get; set; }
    public Guid EventId { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid CompetitionRevision { get; set; }
    public DateTimeOffset DomainEventCreatedAt { get; set; }
    public DateTimeOffset OutboxPersistedAt { get; set; }
    public DateTimeOffset NextDispatchAt { get; set; }
    public DateTimeOffset? WorkerDequeuedAt { get; set; }
    public DateTimeOffset? DispatchCompletedAt { get; set; }
}

[PrimaryKey(nameof(EventId), nameof(TargetId))]
[Index(nameof(CompetitionId), nameof(State), nameof(NextRetryAt))]
public sealed class CompetitionWebhookDeliveryRecord
{
    public Guid EventId { get; set; }
    public Guid TargetId { get; set; }
    public Guid CompetitionId { get; set; }
    public CompetitionWebhookDeliveryState State { get; set; }
    public CompetitionWebhookPayloadState PayloadState { get; set; }
    public DateTimeOffset DomainEventCreatedAt { get; set; }
    public DateTimeOffset OutboxPersistedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextRetryAt { get; set; }
    public DateTimeOffset? EnqueueLeaseUntil { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? WorkerDequeuedAt { get; set; }
    public DateTimeOffset? PublicProjectionReadyAt { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public DateTimeOffset? FirstHttpAttemptStartedAt { get; set; }
    public DateTimeOffset? LastHttpAttemptStartedAt { get; set; }
    public DateTimeOffset? LastHttpAttemptCompletedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int ProjectionRetryCount { get; set; }
    public int HttpRetryCount { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public CompetitionWebhookDeadLetterReason? DeadLetterReason { get; set; }
}
