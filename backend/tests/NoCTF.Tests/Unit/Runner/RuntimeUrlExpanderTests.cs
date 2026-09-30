using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class RuntimeUrlExpanderTests
{
    [Test]
    public async Task Named_entries_resolve_each_services_own_random_port()
    {
        var id = Guid.NewGuid();
        ContainerServiceStatus[] services = [new("web", "web-resource", RuntimeStatus.Running, new Dictionary<int, int> { [8080] = 32000 }, "10.0.0.1"),
            new("admin", "admin-resource", RuntimeStatus.Running, new Dictionary<int, int> { [8080] = 32001 }, "10.0.0.2")];
        var receipt = new ContainerDeploymentReceipt(id, RuntimeProvider.Docker, $"noctf-rt-{id:N}", "", "runner.example", DateTimeOffset.UtcNow, services);
        var expanded = RuntimeUrlExpander.ExpandContainer(receipt, new(receipt.ProjectName, RuntimeStatus.Running, services),
            [new("http://{HOST}:{PORT}/play", RuntimeExposure.OwnerOnly, 8080, "web"), new("nc {HOST} {PORT}", RuntimeExposure.Participants, 8080, "admin")]);
        await Assert.That(expanded.DirectAddresses).IsEquivalentTo(["http://runner.example:32000/play", "nc runner.example 32001"]);
    }

    [Test]
    [Arguments(RuntimeAccessMode.Direct)]
    [Arguments(RuntimeAccessMode.DirectAndWsrx)]
    [Arguments(RuntimeAccessMode.WsrxOnly)]
    public async Task Access_modes_publish_the_correct_direct_and_private_targets(RuntimeAccessMode mode)
    {
        var id = Guid.NewGuid();
        ContainerServiceStatus[] services = [new("main", "resource", RuntimeStatus.Running, new Dictionary<int, int> { [80] = 32000 }, "10.0.0.1")];
        var receipt = new ContainerDeploymentReceipt(id, RuntimeProvider.Docker, $"noctf-rt-{id:N}", "", "runner.example", DateTimeOffset.UtcNow, services);
        var expanded = RuntimeUrlExpander.ExpandContainer(receipt, new(receipt.ProjectName, RuntimeStatus.Running, services),
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "main")], mode);
        var entry = expanded.AccessEndpoints.Single();
        await Assert.That(entry.DirectAddress is not null).IsEqualTo(mode != RuntimeAccessMode.WsrxOnly);
        await Assert.That(entry.TargetHost is not null).IsEqualTo(mode != RuntimeAccessMode.Direct);
        if (mode != RuntimeAccessMode.Direct) await Assert.That(entry.TargetHost).IsEqualTo("10.0.0.1");
    }

    [Test]
    public async Task Unknown_service_or_missing_public_port_is_rejected()
    {
        var id = Guid.NewGuid();
        ContainerServiceStatus[] services = [new("main", "resource", RuntimeStatus.Running, new Dictionary<int, int>(), "10.0.0.1")];
        var receipt = new ContainerDeploymentReceipt(id, RuntimeProvider.Docker, $"noctf-rt-{id:N}", "", "runner.example", DateTimeOffset.UtcNow, services);
        var status = new ContainerRuntimeStatus(receipt.ProjectName, RuntimeStatus.Running, services);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(RuntimeUrlExpander.ExpandContainer(receipt, status,
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "missing")])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(RuntimeUrlExpander.ExpandContainer(receipt, status,
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "main")])));
    }
}
