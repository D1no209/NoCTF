using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awdp.Runtime;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpTargetDefinitionFactoryTests
{
    [Test]
    public async Task Disposable_target_uses_an_isolated_private_container_port()
    {
        var operationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var template = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            "target:latest",
            RuntimeKind: RuntimeKind.Container,
            RunnerPool: "awdp");

        var definition = AwdpTargetDefinitionFactory.Create(
            operationId,
            template,
            targetPort: 8080);

        await Assert.That(definition.NetworkIsolation)
            .IsEqualTo(ContainerNetworkIsolation.Isolated);
        await Assert.That(definition.InternalPorts).IsEquivalentTo([8080]);
        await Assert.That(definition.PortMappings).IsEmpty();
        await Assert.That(definition.Labels["noctf.purpose"]).IsEqualTo("awdp-target");
    }

    [Test]
    [Arguments(RuntimeProvider.Docker, RuntimeKind.Compose)]
    [Arguments(RuntimeProvider.Libvirt, RuntimeKind.Container)]
    [Arguments(RuntimeProvider.Libvirt, RuntimeKind.OvaVm)]
    public async Task Disposable_target_rejects_non_container_or_non_sandbox_provider(
        RuntimeProvider provider,
        RuntimeKind kind)
    {
        var template = new ChallengeRuntimeTemplate(
            provider,
            RuntimeAllocation.PerTeam,
            "target:latest",
            RuntimeKind: kind);

        var action = () => AwdpTargetDefinitionFactory.Create(
            Guid.NewGuid(), template, 8080);

        await Assert.That(action).Throws<InvalidOperationException>();
    }
}
