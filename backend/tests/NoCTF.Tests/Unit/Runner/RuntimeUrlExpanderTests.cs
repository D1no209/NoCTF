using NoCTF.Application.Runtime.Ports;
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

        var expanded = RuntimeUrlExpander.ExpandContainer(receipt, bindings, control);

        await Assert.That(expanded.Urls).IsEquivalentTo(
            ["http://runner.example:32000/owner", "http://runner.example:32000/play"]);
        await Assert.That(expanded.ParticipantUrlIndexes).IsEquivalentTo([1]);
        await Assert.That(expanded.ControlCheckUrl)
            .IsEqualTo("http://container-1:8080/control");
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
}
