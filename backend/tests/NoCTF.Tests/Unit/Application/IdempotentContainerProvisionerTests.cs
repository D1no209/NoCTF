using NSubstitute;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Application;

public sealed class IdempotentContainerProvisionerTests
{
    [Test]
    public async Task Provision_recovers_the_stable_resource_before_creating(CancellationToken cancellationToken)
    {
        var lifecycle = Substitute.For<IContainerLifecycle>();
        var request = CreateRequest();
        var resourceName = $"noctf-{request.OperationId:N}";
        lifecycle.EnsureRunningAsync(request, cancellationToken)
            .Returns(new ContainerReceipt(
                request.OperationId,
                RuntimeProvider.Docker,
                resourceName,
                RuntimeStatus.Running,
                request.PortMappings,
                "runner.example",
                resourceName));

        var receipt = await IdempotentContainerProvisioner.ProvisionAsync(
            lifecycle, request, cancellationToken);

        await Assert.That(receipt.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(receipt.PortMappings).IsEquivalentTo(request.PortMappings);
        await lifecycle.Received(1).EnsureRunningAsync(request, cancellationToken);
    }

    [Test]
    public async Task Provision_creates_when_the_stable_resource_is_absent(CancellationToken cancellationToken)
    {
        var lifecycle = Substitute.For<IContainerLifecycle>();
        var request = CreateRequest();
        lifecycle.EnsureRunningAsync(request, cancellationToken).Returns(new ContainerReceipt(
            request.OperationId,
            request.Provider,
            "created",
            RuntimeStatus.Running,
            request.PortMappings,
            "runner.example",
            "created"));

        var receipt = await IdempotentContainerProvisioner.ProvisionAsync(
            lifecycle, request, cancellationToken);

        await Assert.That(receipt.ResourceId).IsEqualTo("created");
        await lifecycle.Received(1).EnsureRunningAsync(request, cancellationToken);
    }

    private static ContainerRequest CreateRequest() => new(Guid.CreateVersion7(), RuntimeProvider.Docker, "example/runtime:latest", [], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int> { [8080] = 31000 }, new RuntimeResourceLimits(1024, 100, 10), null);
}
