using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Runtime;
using NoCTF.Application.Runtime.Capacity;

namespace NoCTF.Application.Competitions.Awd.Fixes;

public sealed record RunPersistentFix(Guid GameplayFactId, Guid RuntimeInstanceId, DateTimeOffset Deadline, string RunnerId) : IRunnerNodeMessage;
public sealed record ObservePersistentFix(Guid GameplayFactId, Guid RuntimeInstanceId, string RunnerId, long Window) : IRunnerNodeMessage;
public sealed record PersistentFixObservationWork(Guid GameplayFactId, Guid RuntimeInstanceId, ContainerRuntimeReceiptData Receipt,
    string ServiceName, string ExecutionIdentity);
public sealed record PersistentFixArchive(Guid UploadId, string FileName, long ByteLength, byte[] Sha256);
public sealed record PersistentFixWork(Guid GameplayFactId, Guid RuntimeInstanceId, ContainerRuntimeReceiptData Receipt,
    FixCapabilityDefinition Definition, PersistentFixArchive Archive, DateTimeOffset Deadline);

public interface IAwdPersistentFixStore
{
    Task<PersistentFixWork?> ClaimAsync(RunPersistentFix request, DateTimeOffset now, CancellationToken ct);
    Task<bool> CompleteAsync(FixOutcome outcome, DateTimeOffset now, CancellationToken ct);
    Task RecoverAsync(DateTimeOffset now, IRunnerCapacityGate capacity, IRuntimePlacementPolicy placement, CancellationToken ct);
    Task<PersistentFixObservationWork?> ReadObservationAsync(ObservePersistentFix request, CancellationToken ct);
    Task RecordObservationAsync(PersistentFixObservationWork work, ContainerExecObservation observation, DateTimeOffset now, CancellationToken ct);
}

public sealed class RecoverPersistentFixes(IAwdPersistentFixStore store, IRunnerCapacityGate capacity, IRuntimePlacementPolicy placement, TimeProvider clock)
{
    public Task ExecuteAsync(CancellationToken ct) => store.RecoverAsync(clock.GetUtcNow(), capacity, placement, ct);
}

public static class PersistentFixExecutionBudget
{
    public const int ArchiveSeconds = 60;
    public const int CompletionSeconds = 30;
    public const int HandlerSeconds = 4200;
    public const int AckExtensionSeconds = HandlerSeconds + 300;
    public static DateTimeOffset Deadline(DateTimeOffset receivedAt, int fixSeconds) => receivedAt.AddSeconds(ArchiveSeconds + fixSeconds + CompletionSeconds);
}
