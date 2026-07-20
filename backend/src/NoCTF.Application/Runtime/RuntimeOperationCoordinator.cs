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
    CreateFailed,
    ReceiptMismatch
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
        DateTimeOffset staleBefore,
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

public sealed record RuntimeOperationPolicyOptions(
    TimeSpan DefaultOperationTimeout,
    TimeSpan CompensationTimeout,
    TimeSpan ClaimLeaseGrace)
{
    public static TimeSpan MaximumOperationTimeout { get; } = TimeSpan.FromMinutes(5);

    public static RuntimeOperationPolicyOptions Default { get; } = new(
        TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    public void Validate()
    {
        ValidateDuration(DefaultOperationTimeout, nameof(DefaultOperationTimeout));
        ValidateDuration(CompensationTimeout, nameof(CompensationTimeout));
        ValidateDuration(ClaimLeaseGrace, nameof(ClaimLeaseGrace));
    }

    public static void ValidateOperationTimeout(TimeSpan timeout) =>
        ValidateDuration(timeout, "OperationTimeout");

    private static void ValidateDuration(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > MaximumOperationTimeout)
            throw new ArgumentOutOfRangeException(name, value, "Runtime durations must be between 1 tick and 5 minutes.");
    }
}

public sealed class ChallengeRuntimeProvisioner(
    IRuntimeOperationStore operations,
    IContainerLifecycle runtime,
    RuntimeOperationPolicyOptions? configuredOptions = null)
{
    public async Task<ProvisionChallengeRuntimeResult> ExecuteAsync(
        ProvisionChallengeRuntimeCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.OperationKey) || command.OperationKey.Length > 256)
            throw new ArgumentException("Runtime operation key is required and cannot exceed 256 characters.", nameof(command));

        var options = configuredOptions ?? RuntimeOperationPolicyOptions.Default;
        options.Validate();
        var effectiveOperationTimeout = command.OperationTimeout ?? options.DefaultOperationTimeout;
        RuntimeOperationPolicyOptions.ValidateOperationTimeout(effectiveOperationTimeout);
        var now = DateTimeOffset.UtcNow;
        var claimLeaseDuration = RuntimeOperationPolicyOptions.MaximumOperationTimeout + options.ClaimLeaseGrace;
        var begin = await operations.BeginAsync(
            command.CompetitionId,
            command.OperationKey,
            RuntimeOperationKind.CreateContainer,
            now.Subtract(claimLeaseDuration),
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

        var request = command.Container with { OperationId = lease.ClaimToken };
        DateTimeOffset? expiresAt = request.Ttl is { } ttl ? DateTimeOffset.UtcNow.Add(ttl) : null;
        using var operationTimeout = new CancellationTokenSource(effectiveOperationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, operationTimeout.Token);
        var operationToken = linked.Token;
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

        if (receipt.OperationId != lease.ClaimToken)
        {
            var cleanupReceipt = await TryDestroyAsync(receipt);
            _ = await RecordFailureAsync(
                lease, command, expiresAt, RuntimeProvisionFailure.ReceiptMismatch, cleanupReceipt);
            return new(lease.OperationId, RuntimeStatus.Failed, null, false, RuntimeProvisionFailure.ReceiptMismatch);
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
                options.CompensationTimeout);
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
                options.CompensationTimeout);
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
