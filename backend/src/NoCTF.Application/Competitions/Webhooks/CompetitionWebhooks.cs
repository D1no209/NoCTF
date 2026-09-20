using NoCTF.Domain.Competitions;

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
    string? PreviousSigningSecret = null);

public interface ICompetitionWebhookDeliveryStore
{
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

public interface ICompetitionWebhookSender
{
    Task<CompetitionWebhookSendResult> SendAsync(
        CompetitionWebhookDelivery delivery,
        CancellationToken cancellationToken);
}

public sealed class CompetitionWebhookTransientException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

public sealed class CompetitionWebhookPermanentException(
    string message) : Exception(message);

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
