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
    bool HasMore);

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

public interface ICompetitionWebhookStore
{
    Task<CompetitionWebhookTargetPage?> ListAsync(
        Guid competitionId,
        DateTimeOffset? beforeUpdatedAt,
        Guid? beforeId,
        int limit,
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
        DateTimeOffset? beforeUpdatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(competitionId, beforeUpdatedAt, beforeId, limit, cancellationToken);
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
