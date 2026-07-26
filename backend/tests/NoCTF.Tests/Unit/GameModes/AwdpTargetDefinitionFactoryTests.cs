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
            new ContainerRuntimeDefinition("target:latest"),
            RunnerPool: "awdp");

        var definition = AwdpTargetDefinitionFactory.Create(
            operationId,
            generation: 3,
            template,
            targetPort: 8080,
            DateTimeOffset.Parse("2026-07-24T00:00:00Z"));

        await Assert.That(definition.NetworkIsolation)
            .IsEqualTo(ContainerNetworkIsolation.Isolated);
        await Assert.That(definition.InternalPorts).IsEquivalentTo([8080]);
        await Assert.That(definition.PortMappings).IsEmpty();
        await Assert.That(definition.Labels["noctf.io/purpose"]).IsEqualTo("awdp-target");
        await Assert.That(definition.Labels["noctf.io/managed"]).IsEqualTo("true");
        await Assert.That(definition.Labels["noctf.io/generation"]).IsEqualTo("3");
        await Assert.That(definition.Labels["noctf.io/expires-at"]).IsEqualTo("1784852100");
        await Assert.That(definition.Security.CapAdd).IsEmpty();
        await Assert.That(definition.Security.CapDrop).Contains("ALL");
    }

    [Test]
    [Arguments(RuntimeProvider.Docker, RuntimeKind.Compose)]
    [Arguments(RuntimeProvider.Libvirt, RuntimeKind.Container)]
    [Arguments(RuntimeProvider.Libvirt, RuntimeKind.OvaVm)]
    public async Task Disposable_target_rejects_non_container_or_non_sandbox_provider(
        RuntimeProvider provider,
        RuntimeKind kind)
    {
        ChallengeRuntimeDefinition definition = kind switch
        {
            RuntimeKind.Container => new ContainerRuntimeDefinition("target:latest"),
            RuntimeKind.Compose => new ComposeRuntimeDefinition(
                "services:\n  target:\n    image: target:latest"),
            RuntimeKind.OvaVm => new OvaRuntimeDefinition("file:///var/lib/noctf/target.ova"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
        var template = new ChallengeRuntimeTemplate(
            provider,
            RuntimeAllocation.PerTeam,
            definition);

        var action = () => AwdpTargetDefinitionFactory.Create(
            Guid.NewGuid(), 1, template, 8080, DateTimeOffset.UtcNow);

        await Assert.That(action).Throws<InvalidOperationException>();
    }
}
