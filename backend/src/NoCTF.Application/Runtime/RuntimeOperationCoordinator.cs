using System.Text.Json;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeOperationLease(
    Guid OperationId,
    Guid CompetitionId,
    string OperationKey,
    Guid ClaimToken,
    RuntimeStatus Status,
    bool IsNew);

public enum RuntimeOperationBeginFailure
{
    CompetitionNotRunning
}

public sealed record RuntimeOperationBeginResult(
    RuntimeOperationLease? Lease,
    RuntimeOperationBeginFailure? Failure = null);

public enum RuntimeProvisionFailure
{
    CompetitionNotRunning,
    PersistenceRejected,
    CreateCanceled,
    CreateTimeout,
    CreateFailed
}

public sealed record RuntimeOperationFailureContext(
    RuntimeProvisionFailure Failure,
    Guid ChallengeId,
    Guid? TeamId,
    DateTimeOffset? ExpiresAt,
    ContainerReceipt? CleanupReceipt = null);

public sealed record RuntimeOperationFailureResult(ContainerReceipt? ReconciledReceipt = null);

public interface IRuntimeOperationStore
{
    Task<RuntimeOperationBeginResult> BeginAsync(
        Guid competitionId,
        string operationKey,
        RuntimeOperationKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        RuntimeOperationLease lease,
        ContainerReceipt receipt,
        Guid challengeId,
        Guid? teamId,
        DateTimeOffset? expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<RuntimeOperationFailureResult> FailAsync(
        RuntimeOperationLease lease,
        RuntimeOperationFailureContext failure,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record ProvisionChallengeRuntimeCommand(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? TeamId,
    string OperationKey,
    ContainerRequest Container,
    TimeSpan? OperationTimeout = null);

public sealed record ProvisionChallengeRuntimeResult(
    Guid? OperationId,
    RuntimeStatus Status,
    ContainerReceipt? Receipt,
    bool AlreadyExists,
    RuntimeProvisionFailure? Failure = null);

public sealed class ChallengeRuntimeProvisioner(
    IRuntimeOperationStore operations,
    IContainerLifecycle runtime)
{
    private static readonly TimeSpan DefaultCompensationTimeout = TimeSpan.FromSeconds(30);

    public async Task<ProvisionChallengeRuntimeResult> ExecuteAsync(
        ProvisionChallengeRuntimeCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.OperationKey) || command.OperationKey.Length > 256)
            throw new ArgumentException("Runtime operation key is required and cannot exceed 256 characters.", nameof(command));

        var now = DateTimeOffset.UtcNow;
        var begin = await operations.BeginAsync(
            command.CompetitionId,
            command.OperationKey,
            RuntimeOperationKind.CreateContainer,
            now,
            ct);
        if (begin.Lease is null)
            return begin.Failure switch
            {
                RuntimeOperationBeginFailure.CompetitionNotRunning =>
                    new(null, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.CompetitionNotRunning),
                _ => throw new InvalidOperationException("Runtime operation begin returned an invalid result.")
            };
        var lease = begin.Lease;
        if (!lease.IsNew)
            return new(lease.OperationId, lease.Status, null, true);

        var request = command.Container with { OperationId = lease.OperationId };
        DateTimeOffset? expiresAt = request.Ttl is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : null;
        using var operationTimeout = command.OperationTimeout is { } timeout
            ? new CancellationTokenSource(timeout)
            : null;
        using var linked = operationTimeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct, operationTimeout.Token);
        var operationToken = linked?.Token ?? ct;
        ContainerReceipt receipt;
        try
        {
            receipt = await runtime.CreateAsync(request, operationToken);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _ = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.CreateCanceled, null);
            throw;
        }
        catch (OperationCanceledException)
        {
            _ = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.CreateTimeout, null);
            return new(lease.OperationId, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.CreateTimeout);
        }
        catch (Exception)
        {
            _ = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.CreateFailed, null);
            return new(lease.OperationId, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.CreateFailed);
        }

        try
        {
            if (await operations.CompleteAsync(
                    lease,
                    receipt,
                    command.ChallengeId,
                    command.TeamId,
                    expiresAt,
                    DateTimeOffset.UtcNow,
                    ct))
                return new(lease.OperationId, receipt.Status, receipt, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var reconciliation = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.CreateCanceled, receipt);
            if (reconciliation.ReconciledReceipt is { } reconciled)
                return new(lease.OperationId, reconciled.Status, reconciled, true);
            throw;
        }
        catch (Exception)
        {
            var reconciliation = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.PersistenceRejected, receipt);
            if (reconciliation.ReconciledReceipt is { } reconciled)
                return new(lease.OperationId, reconciled.Status, reconciled, true);
            return new(lease.OperationId, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.PersistenceRejected);
        }

        var rejectedCleanupReceipt = await TryDestroyAsync(receipt);
        _ = await RecordFailureAsync(
            lease, command, expiresAt, RuntimeProvisionFailure.PersistenceRejected, rejectedCleanupReceipt);
        return new(lease.OperationId, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.PersistenceRejected);

        async Task<RuntimeOperationFailureResult> RecordFailureAsync(
            RuntimeOperationLease operationLease,
            ProvisionChallengeRuntimeCommand provisionCommand,
            DateTimeOffset? operationExpiresAt,
            RuntimeProvisionFailure failure,
            ContainerReceipt? cleanupReceipt)
        {
            using var compensation = new CancellationTokenSource(
                provisionCommand.OperationTimeout ?? DefaultCompensationTimeout);
            return await operations.FailAsync(
                operationLease,
                new(
                    failure,
                    provisionCommand.ChallengeId,
                    provisionCommand.TeamId,
                    operationExpiresAt,
                cleanupReceipt),
                DateTimeOffset.UtcNow,
                compensation.Token);
        }

        async Task<ContainerReceipt?> TryDestroyAsync(ContainerReceipt createdReceipt)
        {
            using var compensation = new CancellationTokenSource(
                command.OperationTimeout ?? DefaultCompensationTimeout);
            try
            {
                await runtime.DestroyAsync(createdReceipt, compensation.Token);
                return null;
            }
            catch
            {
                return createdReceipt;
            }
        }
    }
}
