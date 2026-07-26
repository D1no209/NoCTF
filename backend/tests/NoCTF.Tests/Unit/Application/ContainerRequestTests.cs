using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class ContainerRequestTests
{
    [Test]
    public async Task Container_ports_include_private_and_host_mapped_ports_once()
    {
        var request = new ContainerRequest(
            Guid.NewGuid(), RuntimeProvider.Docker, "target:latest", [],
            new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<int, int> { [8080] = 18080 },
            new RuntimeResourceLimits(1, 1, 1),
            new ContainerSecurityPolicy(true, true, true, ["ALL"], []),
            null,
            InternalPorts: [8080, 9090]);

        await Assert.That(request.ContainerPorts).IsEquivalentTo([8080, 9090]);
    }
}
