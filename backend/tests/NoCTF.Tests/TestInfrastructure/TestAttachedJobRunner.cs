using NoCTF.Application.Runtime.Provisioning;

namespace NoCTF.Domain.Runtime;

internal sealed class TestAttachedJobRunner(IOneShotJobRunner runner) : IAttachedOneShotJobRunner
{
    public Task<OneShotResult> RunAttachedAsync(ContainerRequest request, AttachedRuntimeTarget target, CancellationToken cancellationToken) => RunAttachedAsync(request, target, null, cancellationToken);
    public Task<OneShotResult> RunAttachedAsync(ContainerRequest request, AttachedRuntimeTarget target, OneShotInputArchive? input, CancellationToken cancellationToken) =>
        runner.RunAsync(request with { NetworkName = ((AttachedContainerRuntimeTarget)target).Receipt.NetworkId ?? "shared" }, input, cancellationToken);
}
