using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Application.Competitions.Webhooks;

public sealed record CompetitionWebhookTargetView(
    Guid Id,
    string Name,
    string EndpointUrl,
    bool Enabled,
    DateTimeOffset? EnabledAt,
    bool SecretConfigured,
    DateTimeOffset? PreviousSecretValidUntil,
    CompetitionWebhookDisabledReason? DisabledReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CompetitionWebhookTargetPage(
    IReadOnlyList<CompetitionWebhookTargetView> Items,
    int Total);

public enum CompetitionWebhookMutationFailure : short
{
    CompetitionNotFound,
    TargetNotFound,
    DuplicateEndpoint,
    InvalidEndpoint,
    InvalidName
}

public sealed record CompetitionWebhookMutationResult(
    CompetitionWebhookTargetView? Target = null,
    string? SigningSecret = null,
    CompetitionWebhookMutationFailure? Failure = null)
{
    public bool Succeeded => Failure is null;
}

public sealed record CreateCompetitionWebhookTargetCommand(
    Guid CompetitionId,
    string Name,
    string EndpointUrl,
    bool Enabled,
    DateTimeOffset Now);

public sealed record UpdateCompetitionWebhookTargetCommand(
    Guid CompetitionId,
    Guid TargetId,
    string Name,
    string EndpointUrl,
    bool Enabled,
    DateTimeOffset Now);

public sealed record DispatchCompetitionWebhooks(
    Guid CompetitionId,
    Guid EventId,
    Guid? AfterTargetId = null);

public sealed record DeliverCompetitionWebhook(
    Guid CompetitionId,
    Guid EventId,
    Guid TargetId,
    DateTimeOffset OccurredAt);

public sealed record TestCompetitionWebhook(
    Guid CompetitionId,
    Guid TargetId,
    Guid DeliveryId,
    DateTimeOffset RequestedAt);

public sealed record CompetitionWebhookDispatchBatch(
    IReadOnlyList<DeliverCompetitionWebhook> Deliveries,
    Guid? NextAfterTargetId = null);

public sealed record CompetitionWebhookOutboxWakeup(
    Guid CompetitionId,
    Guid EventId);

public enum CompetitionWebhookDeliveryState : short
{
    Pending,
    InFlight,
    Delivered,
    Suppressed,
    DeadLetter
}

public enum CompetitionWebhookPayloadState : short
{
    Unknown,
    Complete,
    ProjectionNotReady,
    Invalid
}

public enum CompetitionWebhookDeadLetterReason : short
{
    InvalidPayload,
    ProjectionUnavailable,
    HttpRetryLimitExceeded,
    PermanentHttpFailure
}

public sealed record CompetitionWebhookPendingSnapshot(
    long PendingDeliveries,
    long UndispatchedEvents,
    long OldestPendingAgeSeconds);

public sealed record CompetitionWebhookDeliveryDiagnostic(
    Guid EventId,
    Guid TargetId,
    CompetitionEventKind EventKind,
    CompetitionWebhookDeliveryState State,
    CompetitionWebhookPayloadState PayloadState,
    long EventSequence,
    Guid CompetitionRevision,
    DateTimeOffset DomainEventCreatedAt,
    DateTimeOffset OutboxPersistedAt,
    DateTimeOffset? WorkerDequeuedAt,
    DateTimeOffset? PublicProjectionReadyAt,
    DateTimeOffset? CapturedAt,
    DateTimeOffset? FirstHttpAttemptStartedAt,
    DateTimeOffset? LastHttpAttemptStartedAt,
    DateTimeOffset? LastHttpAttemptCompletedAt,
    int? LastHttpStatusCode,
    int ProjectionRetryCount,
    int HttpRetryCount,
    DateTimeOffset? NextRetryAt,
    CompetitionWebhookDeadLetterReason? DeadLetterReason);

public sealed record CompetitionWebhookDeliveryDiagnosticPage(
    IReadOnlyList<CompetitionWebhookDeliveryDiagnostic> Items,
    int Total);

public enum CompetitionWebhookDeliveryReadState : short
{
    Ready,
    Suppressed,
    Missing
}

public sealed record CompetitionWebhookDelivery(
    CompetitionWebhookDeliveryReadState State,
    Guid CompetitionId,
    Guid EventId,
    Guid TargetId,
    Uri? Endpoint = null,
    byte[]? Body = null,
    string? CurrentSigningSecret = null,
    string? PreviousSigningSecret = null,
    bool RequireNoContent = false);

public interface ICompetitionWebhookDeliveryStore
{
    Task<IReadOnlyList<CompetitionWebhookOutboxWakeup>> ClaimPendingOutboxAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookPendingSnapshot> ReadPendingSnapshotAsync(
        DateTimeOffset now, CancellationToken cancellationToken);

    Task<CompetitionWebhookDeliveryDiagnosticPage?> ListDiagnosticsAsync(
        Guid competitionId, int offset, int limit, bool descending,
        CancellationToken cancellationToken);

    Task MarkDispatchCompletedAsync(Guid eventId, DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliverCompetitionWebhook>> ClaimDueDeliveriesAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task RecordProjectionWaitAsync(DeliverCompetitionWebhook delivery,
        CompetitionWebhookProjectionFailure failure, DateTimeOffset now,
        CancellationToken cancellationToken);

    Task RecordHttpStartedAsync(DeliverCompetitionWebhook delivery,
        DateTimeOffset startedAt, CancellationToken cancellationToken);

    Task RecordHttpCompletedAsync(DeliverCompetitionWebhook delivery,
        CompetitionWebhookSendOutcome outcome, DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    Task RecordHttpFailureAsync(DeliverCompetitionWebhook delivery,
        int? statusCode, DateTimeOffset? retryAfter, bool permanent,
        DateTimeOffset completedAt, CancellationToken cancellationToken);

    Task<CompetitionWebhookDispatchBatch> PrepareBatchAsync(
        DispatchCompetitionWebhooks command,
        int batchSize,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookDelivery> PrepareDeliveryAsync(
        DeliverCompetitionWebhook command,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookDelivery> PrepareTestDeliveryAsync(
        TestCompetitionWebhook command,
        CancellationToken cancellationToken);

    Task DisableGoneAsync(
        Guid competitionId,
        Guid targetId,
        Uri expectedEndpoint,
        DateTimeOffset disabledAt,
        CancellationToken cancellationToken);
}

public enum CompetitionWebhookSendResult : short
{
    Delivered,
    ReceiverGone
}

public sealed record CompetitionWebhookSendOutcome(
    CompetitionWebhookSendResult Result,
    int HttpStatusCode);

public interface ICompetitionWebhookSender
{
    Task<CompetitionWebhookSendOutcome> SendAsync(
        CompetitionWebhookDelivery delivery,
        CancellationToken cancellationToken);
}

public sealed class CompetitionWebhookTransientException(
    string message,
    Exception? innerException = null,
    int? httpStatusCode = null,
    DateTimeOffset? retryAfter = null) : Exception(message, innerException)
{
    public int? HttpStatusCode { get; } = httpStatusCode;
    public DateTimeOffset? RetryAfter { get; } = retryAfter;
}

public sealed class CompetitionWebhookPermanentException(
    string message, int? httpStatusCode = null) : Exception(message)
{
    public int? HttpStatusCode { get; } = httpStatusCode;
}

public enum CompetitionWebhookProjectionFailure : short
{
    MissingEventIdentity,
    MissingPublicProjection,
    MissingPublicChallenge,
    MissingPublicTeam,
    MissingPublicAnnouncement,
    BlackoutSuppressed
}

public sealed class CompetitionWebhookProjectionNotReadyException(
    CompetitionWebhookProjectionFailure failure)
    : Exception($"Webhook public projection is not ready: {failure}.")
{
    public CompetitionWebhookProjectionFailure Failure { get; } = failure;
}

public enum CompetitionWebhookTestState : short
{
    Pending,
    Succeeded,
    Failed
}

public enum CompetitionWebhookTestFailureCode : short
{
    TargetUnavailable,
    ReceiverGone,
    PermanentFailure
}

public sealed record CompetitionWebhookTestStatus(
    Guid DeliveryId,
    Guid CompetitionId,
    Guid TargetId,
    CompetitionWebhookTestState State,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt = null,
    CompetitionWebhookTestFailureCode? FailureCode = null);

public interface ICompetitionWebhookTestStatusStore
{
    Task CreateAsync(CompetitionWebhookTestStatus status, CancellationToken cancellationToken);
    Task<CompetitionWebhookTestStatus?> GetAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task CompleteAsync(
        Guid deliveryId,
        CompetitionWebhookTestState state,
        DateTimeOffset completedAt,
        CompetitionWebhookTestFailureCode? failureCode,
        CancellationToken cancellationToken);
}

public interface ICompetitionWebhookStore
{
    Task<CompetitionWebhookTargetPage?> ListAsync(
        Guid competitionId,
        int offset,
        int limit,
        bool descending,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookMutationResult> CreateAsync(
        CreateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookMutationResult> UpdateAsync(
        UpdateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookMutationResult> RotateSecretAsync(
        Guid competitionId,
        Guid targetId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CompetitionWebhookMutationFailure?> DeleteAsync(
        Guid competitionId,
        Guid targetId,
        CancellationToken cancellationToken);
}

public sealed class ListCompetitionWebhookTargets(ICompetitionWebhookStore store)
{
    public Task<CompetitionWebhookTargetPage?> ExecuteAsync(
        Guid competitionId,
        int offset,
        int limit,
        bool descending,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(competitionId, offset, limit, descending, cancellationToken);
}

public sealed class CreateCompetitionWebhookTarget(ICompetitionWebhookStore store)
{
    public Task<CompetitionWebhookMutationResult> ExecuteAsync(
        CreateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken = default) =>
        store.CreateAsync(command with
        {
            Name = command.Name.Trim(),
            EndpointUrl = command.EndpointUrl.Trim()
        }, cancellationToken);
}

public sealed class UpdateCompetitionWebhookTarget(ICompetitionWebhookStore store)
{
    public Task<CompetitionWebhookMutationResult> ExecuteAsync(
        UpdateCompetitionWebhookTargetCommand command,
        CancellationToken cancellationToken = default) =>
        store.UpdateAsync(command with
        {
            Name = command.Name.Trim(),
            EndpointUrl = command.EndpointUrl.Trim()
        }, cancellationToken);
}

public sealed class RotateCompetitionWebhookSecret(ICompetitionWebhookStore store)
{
    public Task<CompetitionWebhookMutationResult> ExecuteAsync(
        Guid competitionId,
        Guid targetId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        store.RotateSecretAsync(competitionId, targetId, now, cancellationToken);
}

public sealed class DeleteCompetitionWebhookTarget(ICompetitionWebhookStore store)
{
    public Task<CompetitionWebhookMutationFailure?> ExecuteAsync(
        Guid competitionId,
        Guid targetId,
        CancellationToken cancellationToken = default) =>
        store.DeleteAsync(competitionId, targetId, cancellationToken);
}
