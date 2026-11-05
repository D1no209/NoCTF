using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Messages;
using NoCTF.Worker.Runtime;

namespace NoCTF.Tests.Unit.Runner;

public sealed class OvaRuntimePlanTests
{
    [Test]
    public async Task Ova_plan_preserves_source_digest_resources_and_guest_entries()
    {
        var id = Guid.NewGuid(); var digest = new string('a', 64);
        var limits = new RuntimeResourceLimits(256 * 1024 * 1024, 2000, 256);
        RuntimeUrlBinding[] bindings = [new("http://{HOST}:{PORT}/play", RuntimeExposure.Participants, VmId: "web", GuestPort: 8080)];
        var template = new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam, new OvaRuntimeDefinition("https://artifacts.example/challenge.ova", digest),
            limits, TtlSeconds: 3600, OperationTimeoutSeconds: 120, UrlBindings: bindings);
        var runtime = new PlayerRuntimeInstance { Id = id, RuntimeKind = RuntimeKind.OvaVm, RuntimeProvider = RuntimeProvider.Libvirt };
        var message = RuntimeClaimFactory.Create(runtime, "libvirt-runner", GameMode.Ctf, template, null);
        await Assert.That(message).IsTypeOf<ProvisionOvaRuntime>();
        var request = ((ProvisionOvaRuntime)message).Definition;
        await Assert.That(request.OperationId).IsEqualTo(id);
        await Assert.That(request.Sha256).IsEqualTo(digest);
        await Assert.That(request.OvaSource.AbsoluteUri).IsEqualTo("https://artifacts.example/challenge.ova");
        await Assert.That(request.Limits).IsEqualTo(limits);
        await Assert.That(request.Ttl).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(request.UrlBindings).IsEquivalentTo(bindings);
    }

    [Test]
    public async Task Multi_guest_access_entries_resolve_the_selected_guest_and_reject_unknown_targets()
    {
        var receipt = new OvaRuntimeReceipt(Guid.NewGuid(), RuntimeProvider.Libvirt, "appliance-network", "10.90.0.0/28",
            [new("web", "web-domain", "10.90.0.2"), new("database", "database-domain", "10.90.0.3")], DateTimeOffset.UtcNow);
        var expanded = RuntimeUrlExpander.ExpandOva(receipt,
            [new("http://{HOST}:{PORT}/play", RuntimeExposure.OwnerOnly, VmId: "web", GuestPort: 8080),
                new("ssh {HOST}", RuntimeExposure.OwnerOnly, VmId: "database")]);
        await Assert.That(expanded.DirectAddresses).IsEquivalentTo(new[] { "http://10.90.0.2:8080/play", "ssh 10.90.0.3" });
        await Assert.That(() => RuntimeUrlExpander.ExpandOva(receipt,
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, VmId: "missing", GuestPort: 8080)])).Throws<InvalidOperationException>();
        await Assert.That(() => RuntimeUrlExpander.ExpandOva(receipt,
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, VmId: "web")])).Throws<InvalidOperationException>();
    }
}
