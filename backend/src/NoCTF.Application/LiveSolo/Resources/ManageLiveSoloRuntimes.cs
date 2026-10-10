using NoCTF.Application.Runtime.Instances;

namespace NoCTF.Application.LiveSolo.Resources;

public sealed record LiveSoloRuntimeCommand(LiveSoloResourceRequest Scope, RuntimeAction Action, Guid? ExpectedRuntimeInstanceId);
public interface ILiveSoloRuntimeStore
{
    Task<RuntimeInstanceView?> ReadAsync(LiveSoloResourceRequest request, CancellationToken ct);
    Task<RuntimeMutationResult> MutateAsync(LiveSoloRuntimeCommand command, CancellationToken ct);
}
public sealed class ManageLiveSoloRuntimes(ILiveSoloRuntimeStore store)
{
    public Task<RuntimeMutationResult> ExecuteAsync(LiveSoloRuntimeCommand command, CancellationToken ct) =>
        !Enum.IsDefined(command.Action) || command.Action == RuntimeAction.Extend
            || command.Action is RuntimeAction.Reset or RuntimeAction.Stop && command.ExpectedRuntimeInstanceId is null
            || command.ExpectedRuntimeInstanceId == Guid.Empty
        ? Task.FromResult(new RuntimeMutationResult(null, RuntimeMutationFailure.Unsupported))
        : store.MutateAsync(command, ct);
}
