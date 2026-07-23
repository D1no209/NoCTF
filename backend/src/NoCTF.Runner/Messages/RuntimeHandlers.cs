using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Runner.Composition;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;

namespace NoCTF.Runner.Messages;

public sealed class RuntimeProviderHandler(
    RuntimeProviderCatalog providers,
    IConfiguration configuration,
    IRunnerCapacityGate capacity)
{
    public async Task<object> Handle(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken)
    {
        ValidatePool(message.RunnerPool);
        if (!string.Equals(message.RunnerId, ReadRunnerId(), StringComparison.Ordinal))
            throw new InvalidOperationException("Runtime message was assigned to a different Runner.");
        try
        {
            var receipt = await providers.Containers(message.Definition.Provider)
                .CreateAsync(message.Definition, cancellationToken);
            return new RuntimeProvisioned(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                ReadRunnerId(),
                receipt.Provider,
                JsonSerializer.Serialize(receipt),
                [],
                []);
        }
        catch (TimeoutException)
        {
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProvisionTimeout,
                message.RunnerId);
        }
        catch (InvalidOperationException)
        {
            await ReleaseCapacityAsync(message, cancellationToken);
            return new RuntimeProvisionFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.ProviderRejected,
                message.RunnerId);
        }
    }

    public async Task<object> Handle(
        StopContainerRuntime message,
        CancellationToken cancellationToken)
    {
        ValidatePool(message.RunnerPool);
        try
        {
            var receipt = JsonSerializer.Deserialize<ContainerReceipt>(message.ProviderReceiptJson)
                ?? throw new InvalidOperationException("Provider receipt is invalid.");
            await providers.Containers(message.Provider).DestroyAsync(receipt, cancellationToken);
            await capacity.ReleaseAsync(message.RuntimeInstanceId, ReadRunnerId(), cancellationToken);
            return new RuntimeStopped(message.RuntimeInstanceId, message.ProcessingVersion);
        }
        catch (InvalidOperationException)
        {
            return new RuntimeStopFailed(
                message.RuntimeInstanceId,
                message.ProcessingVersion,
                RuntimeFailureCode.CleanupFailed);
        }
    }

    private void ValidatePool(string messagePool)
    {
        var configuredPool = configuration["Runner:Pool"] ?? "default";
        if (!string.Equals(configuredPool, messagePool, StringComparison.Ordinal)
            || RunnerQueueName.FromPool(messagePool) != RunnerQueueName.FromPool(configuredPool))
        {
            throw new InvalidOperationException("Runtime message was delivered to the wrong Runner pool.");
        }
    }

    private string ReadRunnerId() => configuration["Runner:Id"]
        ?? throw new InvalidOperationException("Runner:Id is required.");

    private Task ReleaseCapacityAsync(
        ProvisionContainerRuntime message,
        CancellationToken cancellationToken) =>
        capacity.ReleaseAsync(message.RuntimeInstanceId, message.RunnerId, cancellationToken);
}

public static class RuntimeWriteBackHandler
{
    public static async Task Handle(
        RuntimeProvisioned message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning)
        {
            return;
        }

        instance.RunnerId = message.RunnerId;
        instance.ProviderReceiptJson = message.ProviderReceiptJson;
        instance.Urls = [.. message.Urls];
        instance.ParticipantUrlIndexes = [.. message.ParticipantUrlIndexes];
        instance.State = RuntimeState.Running;
        instance.RunningAt = DateTimeOffset.UtcNow;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeProvisionFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Provisioning)
        {
            return;
        }

        instance.State = RuntimeState.Failed;
        instance.FailureCode = message.FailureCode;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeStopped message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Stopping)
            return;

        instance.State = RuntimeState.Stopped;
        instance.StoppedAt = DateTimeOffset.UtcNow;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task Handle(
        RuntimeStopFailed message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var instance = await db.RuntimeInstances.SingleOrDefaultAsync(
            candidate => candidate.Id == message.RuntimeInstanceId,
            cancellationToken);
        if (instance is null
            || instance.ProcessingVersion != message.ProcessingVersion
            || instance.State != RuntimeState.Stopping)
            return;

        instance.State = RuntimeState.Failed;
        instance.FailureCode = message.FailureCode;
        instance.ProcessingVersion = checked(instance.ProcessingVersion + 1);
        await db.SaveChangesAsync(cancellationToken);
    }
}
