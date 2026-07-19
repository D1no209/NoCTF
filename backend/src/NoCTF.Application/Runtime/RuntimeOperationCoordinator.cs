using System.Text.Json;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.Runtime;

public sealed record RuntimeOperationLease(
    Guid OperationId,
    Guid CompetitionId,
    string OperationKey,
    RuntimeStatus Status,
    bool IsNew);

public interface IRuntimeOperationStore
{
    Task<RuntimeOperationLease> BeginAsync(
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
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task FailAsync(
        RuntimeOperationLease lease,
        string errorCode,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public sealed record ProvisionChallengeRuntimeCommand(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? TeamId,
    string OperationKey,
    ContainerRequest Container);

public sealed record ProvisionChallengeRuntimeResult(
    Guid OperationId,
    RuntimeStatus Status,
    ContainerReceipt? Receipt,
    bool AlreadyCompleted);

public sealed class ChallengeRuntimeProvisioner(
    IRuntimeOperationStore operations,
    IContainerLifecycle runtime)
{
    public async Task<ProvisionChallengeRuntimeResult> ExecuteAsync(
        ProvisionChallengeRuntimeCommand command,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.OperationKey) || command.OperationKey.Length > 256)
            throw new ArgumentException("Runtime operation key is required and cannot exceed 256 characters.", nameof(command));

        var now = DateTimeOffset.UtcNow;
        var lease = await operations.BeginAsync(
            command.CompetitionId,
            command.OperationKey,
            RuntimeOperationKind.CreateContainer,
            now,
            ct);
        if (!lease.IsNew && lease.Status is RuntimeStatus.Running or RuntimeStatus.Stopped)
            return new(lease.OperationId, lease.Status, null, true);

        var request = command.Container with { OperationId = lease.OperationId };
        try
        {
            var receipt = await runtime.CreateAsync(request, ct);
            if (!await operations.CompleteAsync(
                    lease,
                    receipt,
                    command.ChallengeId,
                    command.TeamId,
                    DateTimeOffset.UtcNow,
                    ct))
            {
                await runtime.DestroyAsync(receipt, ct);
                return new(lease.OperationId, RuntimeStatus.Failed, null, false);
            }
            return new(lease.OperationId, receipt.Status, receipt, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await operations.FailAsync(lease, "runtime_create_failed", DateTimeOffset.UtcNow, ct);
            return new(lease.OperationId, RuntimeStatus.Failed, null, false);
        }
    }
}
