using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.PublicAccess;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Platform;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class PublicGatewayPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-08T00:00:00Z");
    private static readonly PublicGatewayPolicy Policy = new(true, "gateway", "https://challenge.example.test",
        ["https://direct.example.test"], "203.0.113.1", null, 8);
    private static readonly PublicGatewayCapability Capability = new("gateway", "runner", [Policy.PublicOrigin],
        32768, 60999, [36632], 8, true);
    private static readonly RuntimeInstanceView Runtime = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        Guid.NewGuid(), RuntimePurpose.Player, RuntimeKind.Container, RuntimeProvider.Docker, RuntimeState.Running,
        null, ["nc internal.example.test 32777"], Now, Now, Now.AddMinutes(10), null, "runner",
        [new(null, 31337, 32777)]);

    [Test]
    public async Task Unconfigured_and_direct_routes_preserve_existing_urls_without_gateway_state()
    {
        await Assert.That(PublicGatewayPolicyRules.Route(PublicGatewayPolicy.Disabled, "https://existing.test"))
            .IsEqualTo(RuntimeAccessRoute.Direct);
        var result = PublicGatewayPolicyRules.Project(PublicGatewayPolicy.Disabled, RuntimeAccessRoute.Direct,
            null, Runtime, GameMode.Ctf, [], null, Now);
        await Assert.That(result.Urls).IsEquivalentTo(Runtime.Urls);
    }

    [Test]
    public async Task Origins_are_exact_and_disabling_public_access_never_leaks_direct_urls()
    {
        await Assert.That(PublicGatewayPolicyRules.Route(Policy, Policy.PublicOrigin)).IsEqualTo(RuntimeAccessRoute.Gateway);
        await Assert.That(PublicGatewayPolicyRules.Route(Policy, "https://challenge.example.test.attacker.test")).IsNull();
        await Assert.That(PublicGatewayPolicyRules.Route(Policy, "http://challenge.example.test")).IsNull();
        var result = Project(Policy with { Enabled = false }, Runtime, Ready());
        await Assert.That(result.Failure).IsEqualTo(PublicAccessFailure.GatewayDisabled);
        await Assert.That(result.Urls).IsEmpty();
    }

    [Test]
    [Arguments(RuntimePurpose.AwdpTarget, RuntimeKind.Container, RuntimeProvider.Docker)]
    [Arguments(RuntimePurpose.TemplateTest, RuntimeKind.Container, RuntimeProvider.Docker)]
    [Arguments(RuntimePurpose.Player, RuntimeKind.Compose, RuntimeProvider.Docker)]
    [Arguments(RuntimePurpose.Player, RuntimeKind.Container, RuntimeProvider.Kubernetes)]
    public async Task Unapproved_runtime_types_never_publish(RuntimePurpose purpose, RuntimeKind kind, RuntimeProvider provider)
    {
        var result = Project(Policy, Runtime with { Purpose = purpose, RuntimeKind = kind, Provider = provider }, Ready());
        await Assert.That(result.State).IsEqualTo(PublicAccessState.Unsupported);
        await Assert.That(result.Urls).IsEmpty();
    }

    [Test]
    public async Task Gateway_status_requires_matching_identity_and_unexpired_lease()
    {
        foreach (var status in new[] { Ready() with { RuntimeId = Guid.NewGuid() }, Ready() with { RunnerId = "other" },
                     Ready() with { ConnectorId = "other" }, Ready() with { ValidUntil = Now } })
            await Assert.That(Project(Policy, Runtime, status).Urls).IsEmpty();
        await Assert.That(Project(Policy, Runtime with { State = RuntimeState.Stopped }, Ready()).Urls).IsEmpty();
    }

    [Test]
    public async Task Text_templates_use_actual_logical_ports_and_not_url_list_indexes()
    {
        var bindings = new[] { new RuntimeUrlBinding("nc {HOST} {PORT}", RuntimeExposure.OwnerOnly, ContainerPort: 31337),
            new RuntimeUrlBinding("Connect: {HOST}:{PORT}", RuntimeExposure.OwnerOnly, ContainerPort: 31337) };
        var result = PublicGatewayPolicyRules.Project(Policy, RuntimeAccessRoute.Gateway, Capability, Runtime,
            GameMode.Ctf, bindings, Ready(), Now);
        await Assert.That(result.Urls).IsEquivalentTo(["nc 203.0.113.1 32777", "Connect: 203.0.113.1:32777"]);
        await Assert.That(result.Endpoints.Count).IsEqualTo(1);
        await Assert.That(result.State).IsEqualTo(PublicAccessState.Ready);
    }

    [Test]
    public async Task Deployment_limits_and_origin_validation_reject_unsafe_configuration()
    {
        await Assert.That(PublicGatewayPolicyRules.Validate(Policy, Capability)).IsEmpty();
        foreach (var policy in new[] { Policy with { MaxPublishedPorts = 9 }, Policy with { ConnectorId = "unknown" },
                     Policy with { PublicOrigin = "https://user:password@challenge.example.test" },
                     Policy with { PublicRuntimeHost = "127.0.0.1;sh" }, Policy with { DirectOrigins = [Policy.PublicOrigin] } })
            await Assert.That(PublicGatewayPolicyRules.Validate(policy, Capability).Count).IsGreaterThan(0);
        await Assert.That(PublicGatewayPolicyRules.Validate(Policy, Capability with { NamespaceIsolationAvailable = false }).Count).IsGreaterThan(0);
        await Assert.That(Project(Policy, Runtime with { PublishedPorts = [new(null, 31337, 36632)] }, Ready()).Urls).IsEmpty();
    }

    private static PublicRuntimeStatus Ready() => new(Runtime.Id, "gateway", "runner", Now.AddSeconds(5),
        [new(31337, 32777, PublicAccessState.Ready, null)]);

    [Test]
    public async Task Direct_override_expands_templates_without_rewriting_stored_urls()
    {
        var result = PublicGatewayPolicyRules.Project(Policy with { DirectRuntimeHostOverride = "192.0.2.1" }, RuntimeAccessRoute.Direct,
            Capability, Runtime, GameMode.Ctf, [new("nc {HOST} {PORT}", RuntimeExposure.OwnerOnly, 31337)], null, Now);
        await Assert.That(result.Urls).IsEquivalentTo(["nc 192.0.2.1 32777"]);
        await Assert.That(Runtime.Urls).IsEquivalentTo(["nc internal.example.test 32777"]);
    }
    private static RuntimeAccessProjection Project(PublicGatewayPolicy policy, RuntimeInstanceView runtime, PublicRuntimeStatus? status) =>
        PublicGatewayPolicyRules.Project(policy, RuntimeAccessRoute.Gateway, Capability, runtime, GameMode.Ctf,
            [new("nc {HOST} {PORT}", RuntimeExposure.OwnerOnly, ContainerPort: 31337)], status, Now);
}
