using Microsoft.Extensions.Options;
using NoCTF.Application.Competitions.Awd.Fixes;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Runner.Competitions.Awd.Fixes;

[NonTransactional]
public sealed class ObservePersistentFixHandler(IAwdPersistentFixStore store, IRuntimeProviderCatalog providers,
    IRunnerCapacityGate capacity, RunnerResourceMutationCoordinator mutations, IOptions<RunnerOptions> runner, TimeProvider clock)
{
    public async Task Handle(ObservePersistentFix message, CancellationToken ct)
    {
        RunnerNodeAssignmentGuard.Validate(message, runner.Value.Pool, runner.Value.Id);
        using var targetMutation = await mutations.EnterRuntimeAsync(message.RuntimeInstanceId, ct);
        if (!await capacity.CanMutateAsync(message.RuntimeInstanceId, runner.Value.Id, ct)) return;
        var work = await store.ReadObservationAsync(message, ct);
        if (work is null) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var observation = await providers.Sandbox(work.Receipt.Provider).ObserveExecAsync(work.Receipt.ServiceReceipt(work.ServiceName), work.ExecutionIdentity, deadline.Token);
        await store.RecordObservationAsync(work, observation, clock.GetUtcNow(), ct);
    }
}
