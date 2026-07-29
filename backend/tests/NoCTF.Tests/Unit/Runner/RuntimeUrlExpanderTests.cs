using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RuntimeUrlExpanderTests
{
    [Test]
    public async Task Container_bindings_use_public_mapping_and_private_control_address()
    {
        var receipt = new ContainerReceipt(
            Guid.CreateVersion7(),
            RuntimeProvider.Docker,
            "container-1",
            RuntimeStatus.Running,
            new Dictionary<int, int> { [8080] = 32000 },
            "runner.example",
            "container-1");
        RuntimeUrlBinding[] bindings =
        [
            new("http://{HOST}:{PORT}/owner", RuntimeExposure.OwnerOnly, ContainerPort: 8080),
            new("http://{HOST}:{PORT}/play", RuntimeExposure.Participants, ContainerPort: 8080)
        ];
        var control = new RuntimeUrlBinding(
            "http://{HOST}:{PORT}/control",
            RuntimeExposure.OwnerOnly,
            ContainerPort: 8080);

        var expanded = RuntimeUrlExpander.ExpandContainer(
            receipt,
            bindings,
            control,
            new RuntimeInternalEndpointBinding());

        await Assert.That(expanded.Urls).IsEquivalentTo(
            ["http://runner.example:32000/owner", "http://runner.example:32000/play"]);
        await Assert.That(expanded.ParticipantUrlIndexes).IsEquivalentTo([1]);
        await Assert.That(expanded.ControlCheckUrl)
            .IsEqualTo("http://container-1:8080/control");
        await Assert.That(expanded.AwdCheckerTargetHost)
            .IsEqualTo("container-1");
    }

    [Test]
    public async Task Missing_dynamic_port_mapping_is_rejected()
    {
        var receipt = new ContainerReceipt(
            Guid.CreateVersion7(),
            RuntimeProvider.Docker,
            "container-1",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            "runner.example",
            "container-1");

        await Assert.That(() => RuntimeUrlExpander.ExpandContainer(
                receipt,
                [new("http://{HOST}:{PORT}", RuntimeExposure.Participants, ContainerPort: 8080)],
                null))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Compose_bindings_resolve_the_named_service()
    {
        var receipt = new ComposeReceipt(
            Guid.CreateVersion7(),
            RuntimeProvider.Docker,
            "noctf-runtime",
            "/tmp/noctf-runtime",
            "runner.example",
            2,
            DateTimeOffset.UtcNow);
        var status = new ComposeStatus(
            receipt.ProjectName,
            RuntimeStatus.Running,
            [
                new(
                    "web",
                    "container-web",
                    RuntimeStatus.Running,
                    new Dictionary<int, int> { [8080] = 32000 },
                    "web")
            ]);
        RuntimeUrlBinding[] bindings =
        [
            new(
                "http://{HOST}:{PORT}/play",
                RuntimeExposure.Participants,
                ContainerPort: 8080,
                ServiceName: "web")
        ];
        var control = new RuntimeUrlBinding(
            "http://{HOST}:{PORT}/control",
            RuntimeExposure.OwnerOnly,
            ContainerPort: 8080,
            ServiceName: "web");

        var expanded = RuntimeUrlExpander.ExpandCompose(
            receipt,
            status,
            bindings,
            control,
            new RuntimeInternalEndpointBinding("web"));

        await Assert.That(expanded.Urls)
            .IsEquivalentTo(["http://runner.example:32000/play"]);
        await Assert.That(expanded.ParticipantUrlIndexes).IsEquivalentTo([0]);
        await Assert.That(expanded.ControlCheckUrl)
            .IsEqualTo("http://web:8080/control");
        await Assert.That(expanded.AwdCheckerTargetHost).IsEqualTo("web");
    }
}
