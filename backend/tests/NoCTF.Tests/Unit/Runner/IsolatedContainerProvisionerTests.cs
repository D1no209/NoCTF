using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class IsolatedContainerProvisionerTests
{
    [Test]
    public async Task Isolated_target_receipt_keeps_the_network_for_node_owned_cleanup()
    {
        var lifecycle = new RecordingLifecycle();
        var sandbox = new RecordingSandbox();
        var request = Request();

        var receipt = await IsolatedContainerProvisioner.ProvisionAsync(
            lifecycle, sandbox, request, DateTimeOffset.Parse("2026-07-24T00:00:00Z"), CancellationToken.None);

        await Assert.That(lifecycle.Request!.NetworkName).IsEqualTo("network-1");
        await Assert.That(receipt.NetworkId).IsEqualTo("network-1");
        await Assert.That(sandbox.DeletedNetworks).IsEmpty();
        await Assert.That(sandbox.Request!.Purpose)
            .IsEqualTo(ContainerNetworkPurpose.PersistentRuntime);
        await Assert.That(sandbox.Request.PublicIngressPorts).IsEquivalentTo([8080]);
    }

    [Test]
    public async Task Failed_target_creation_removes_the_isolated_network()
    {
        var lifecycle = new RecordingLifecycle { Failure = new InvalidOperationException("failed") };
        var sandbox = new RecordingSandbox();

        var action = async () => await IsolatedContainerProvisioner.ProvisionAsync(
            lifecycle, sandbox, Request(), DateTimeOffset.UtcNow, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(sandbox.DeletedNetworks).IsEquivalentTo(["network-1"]);
    }

    [Test]
    public async Task Failed_target_creation_uses_a_fresh_token_for_network_cleanup()
    {
        using var cancellation = new CancellationTokenSource();
        var lifecycle = new RecordingLifecycle
        {
            BeforeFailure = cancellation.Cancel,
            Failure = new OperationCanceledException(cancellation.Token)
        };
        var sandbox = new RecordingSandbox();

        var action = async () => await IsolatedContainerProvisioner.ProvisionAsync(
            lifecycle, sandbox, Request(), DateTimeOffset.UtcNow, cancellation.Token);

        await Assert.That(action).Throws<OperationCanceledException>();
        await Assert.That(sandbox.CleanupTokenWasCancelled).IsFalse();
        await Assert.That(sandbox.DeletedNetworks).IsEquivalentTo(["network-1"]);
    }

    [Test]
    public async Task Destroy_succeeds_when_cleanup_warning_is_followed_by_confirmed_absence()
    {
        var lifecycle = new RecordingLifecycle
        {
            DestroyFailure = new InvalidOperationException("container cleanup failed")
        };
        var sandbox = new RecordingSandbox();
        var receipt = new ContainerReceipt(
            Guid.NewGuid(), RuntimeProvider.Docker, "target", RuntimeStatus.Running,
            new Dictionary<int, int>(), null, "target.internal", "network-1");

        await IsolatedContainerProvisioner.DestroyAsync(
            lifecycle, sandbox, receipt, CancellationToken.None);
        await Assert.That(sandbox.DeletedNetworks).IsEquivalentTo(["network-1"]);
    }

    private static ContainerRequest Request() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        RuntimeProvider.Docker,
        "target:latest",
        [],
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<int, int> { [8080] = 0 },
        new RuntimeResourceLimits(1, 1, 1),
        new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
        null,
        RuntimeInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        NetworkIsolation: ContainerNetworkIsolation.Isolated);

    private sealed class RecordingLifecycle : IContainerLifecycle
    {
        public ContainerRequest? Request { get; private set; }
        public Exception? Failure { get; init; }
        public Action? BeforeFailure { get; init; }
        public Exception? DestroyFailure { get; init; }
        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ContainerReceipt> EnsureRunningAsync(
            ContainerRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            BeforeFailure?.Invoke();
            return Failure is null
                ? Task.FromResult(new ContainerReceipt(
                    request.OperationId, request.Provider, "target", RuntimeStatus.Running,
                    request.PortMappings, null, "target.internal"))
                : Task.FromException<ContainerReceipt>(Failure);
        }
        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) =>
            DestroyFailure is null ? Task.CompletedTask : Task.FromException(DestroyFailure);
        public Task<ContainerReceipt?> GetAsync(
            RuntimeProvider provider,
            string resourceId,
            CancellationToken cancellationToken) => Task.FromResult<ContainerReceipt?>(null);
    }

    private sealed class RecordingSandbox : IContainerSandboxLifecycle
    {
        public List<string> DeletedNetworks { get; } = [];
        public bool CleanupTokenWasCancelled { get; private set; }
        public ContainerNetworkPolicyRequest? Request { get; private set; }
        public Task<string> CreateIsolatedNetworkAsync(
            ContainerNetworkPolicyRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult("network-1");
        }
        public Task DeleteIsolatedNetworkAsync(string networkId, CancellationToken cancellationToken)
        {
            CleanupTokenWasCancelled = cancellationToken.IsCancellationRequested;
            DeletedNetworks.Add(networkId);
            return Task.CompletedTask;
        }

        public Task<bool> IsolatedNetworkExistsAsync(
            string networkId,
            CancellationToken cancellationToken) => Task.FromResult(false);
        public Task CopyArchiveAsync(ContainerReceipt receipt, Stream tarArchive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ContainerExecResult> ExecAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ContainerExecResult> ExecWithInputAsync(
            ContainerReceipt receipt,
            IReadOnlyList<string> command,
            ReadOnlyMemory<byte> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
