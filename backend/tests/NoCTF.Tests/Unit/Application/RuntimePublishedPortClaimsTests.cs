using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class RuntimePublishedPortClaimsTests
{
    [Test]
    public async Task Container_claim_applies_exact_platform_allocations()
    {
        var claim = new ClaimContainerRuntime(
            Guid.NewGuid(),
            0,
            1,
            "docker",
            Container(new Dictionary<int, int>
            {
                [8080] = 0,
                [8443] = 0
            }));

        var targets = RuntimePublishedPortClaims.Targets(claim);
        var mapped = (ClaimContainerRuntime)RuntimePublishedPortClaims.Apply(
            claim,
            [
                new(null, 8080, RuntimePublishedPortRange.StartPort),
                new(null, 8443, RuntimePublishedPortRange.EndPort)
            ]);

        await Assert.That(targets)
            .IsEquivalentTo([
                new RuntimePublishedPortTarget(null, 8080),
                new RuntimePublishedPortTarget(null, 8443)
            ]);
        await Assert.That(mapped.Definition.PortMappings[8080])
            .IsEqualTo(RuntimePublishedPortRange.StartPort);
        await Assert.That(mapped.Definition.PortMappings[8443])
            .IsEqualTo(RuntimePublishedPortRange.EndPort);
    }

    [Test]
    public async Task Compose_claim_deduplicates_public_targets_and_carries_exact_mappings()
    {
        var operationId = Guid.NewGuid();
        var definition = new ComposeRequest(
            operationId,
            RuntimeProvider.Docker,
            1,
            $"runtime-{operationId:N}",
            "services: {}",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, RuntimeResourceLimits>(),
            new(1, 1, 1),
            null,
            TimeSpan.FromMinutes(1),
            [
                new("http://{HOST}:{PORT}", RuntimeExposure.Participants, 8080, "web"),
                new("tcp://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 8080, "web")
            ],
            new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 9090, "admin"));
        var claim = new ClaimComposeRuntime(operationId, 0, 1, "docker", definition);

        var targets = RuntimePublishedPortClaims.Targets(claim);
        var mappings = new RuntimePublishedPortMapping[]
        {
            new("admin", 9090, 61000),
            new("web", 8080, 61001)
        };
        var mapped = (ClaimComposeRuntime)RuntimePublishedPortClaims.Apply(claim, mappings);

        await Assert.That(targets).IsEquivalentTo([
            new RuntimePublishedPortTarget("admin", 9090),
            new RuntimePublishedPortTarget("web", 8080)
        ]);
        await Assert.That(mapped.Definition.PublishedPorts).IsEquivalentTo(mappings);
    }

    [Test]
    public async Task Production_range_includes_only_selected_high_ports()
    {
        var range = new RuntimePublishedPortRange();

        await Assert.That(range.Count).IsEqualTo(4000);
        await Assert.That(range.Contains(60999)).IsFalse();
        await Assert.That(range.Contains(61000)).IsTrue();
        await Assert.That(range.Contains(64999)).IsTrue();
        await Assert.That(range.Contains(65000)).IsFalse();
    }

    private static ContainerRequest Container(IReadOnlyDictionary<int, int> ports) => new(
        Guid.NewGuid(),
        RuntimeProvider.Docker,
        "example/runtime:latest",
        [],
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        ports,
        new(1, 1, 1),
        new(true, true, true, ["ALL"], []),
        null);
}
