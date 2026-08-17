using System.Text.Json;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Worker.Runtime;

namespace NoCTF.Tests.Unit.Worker;

public sealed class RuntimeClaimFactoryTests
{
    [Test]
    public async Task Container_definition_creates_only_a_container_claim()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 },
                EgressPolicy: RuntimeEgressPolicy.Isolated),
            Limits: new(268_435_456, 500_000_000, 128));

        var claim = RuntimeClaimFactory.Create(instance, GameMode.Ctf, template, "{}");

        await Assert.That(claim).IsTypeOf<ClaimContainerRuntime>();
        var container = (ClaimContainerRuntime)claim;
        await Assert.That(container.Definition.Image).IsEqualTo("challenge:v1");
        await Assert.That(container.Definition.RuntimeInstanceId).IsEqualTo(instance.Id);
        await Assert.That(container.Definition.Generation).IsEqualTo(instance.Generation);
        await Assert.That(container.Definition.NetworkIsolation)
            .IsEqualTo(ContainerNetworkIsolation.Isolated);
        await Assert.That(container.Definition.EgressPolicy)
            .IsEqualTo(RuntimeEgressPolicy.Isolated);
        await Assert.That(container.Definition.PortMappings[8080]).IsEqualTo(0);
        await Assert.That(container.Definition.Security.NoNewPrivileges).IsFalse();
        await Assert.That(container.Definition.Security.ReadonlyRootfs).IsFalse();
        await Assert.That(container.Definition.Security.RunAsNonRoot).IsFalse();
        await Assert.That(container.Definition.Security.CapDrop).IsEmpty();
        await Assert.That(container.Definition.Security.CapAdd).IsEmpty();
        await Assert.That(container.Definition.Labels["noctf.io/job-kind"])
            .IsEqualTo("persistent-runtime");
    }

    [Test]
    public async Task Container_security_treats_null_cap_add_as_empty()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "challenge:v1",
                Security: new ContainerSecurityPolicy(
                    true,
                    true,
                    true,
                    ["ALL"],
                    null!)));

        var claim = (ClaimContainerRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Ctf,
            template,
            "{}");

        await Assert.That(claim.Definition.Security.CapAdd).IsEmpty();
    }

    [Test]
    public async Task Ctf_per_team_container_overrides_the_configured_flag_value()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "challenge:v1",
                Environment: new Dictionary<string, string> { ["FLAG"] = "author-value" },
                FlagEnvironmentVariableName: "FLAG"),
            FlagSource: RuntimeFlagSource.PerTeam);

        var claim = (ClaimContainerRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Ctf,
            template,
            "{}",
            "flag{fixed-team}");

        await Assert.That(claim.Definition.Environment["FLAG"])
            .IsEqualTo("flag{fixed-team}");
    }

    [Test]
    public async Task Awdp_per_team_container_uses_the_generation_flag_and_owner_only_url()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        instance.Purpose = RuntimePurpose.Player;
        var ownerOnlyUrl = new RuntimeUrlBinding(
            "tcp://{HOST}:{PORT}",
            RuntimeExposure.OwnerOnly,
            31337);
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "awdp-target:latest",
                Environment: new Dictionary<string, string> { ["FLAG"] = "author-value" },
                PortMappings: new Dictionary<int, int> { [31337] = 0 },
                FlagEnvironmentVariableName: "FLAG",
                InternalPorts: [31337]),
            UrlBindings: [ownerOnlyUrl],
            FlagSource: RuntimeFlagSource.PerTeam);

        var claim = (ClaimContainerRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Awdp,
            template,
            "{}",
            "flag{generation-one}");

        await Assert.That(claim.Definition.Environment["FLAG"])
            .IsEqualTo("flag{generation-one}");
        await Assert.That(claim.Definition.PortMappings[31337]).IsEqualTo(0);
        await Assert.That(claim.Definition.UrlBindings).IsEquivalentTo([ownerOnlyUrl]);
    }

    [Test]
    public async Task Awdp_per_team_container_rejects_a_missing_generation_flag()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        instance.Purpose = RuntimePurpose.Player;
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "awdp-target:latest",
                FlagEnvironmentVariableName: "FLAG"),
            FlagSource: RuntimeFlagSource.PerTeam);

        var action = () => RuntimeClaimFactory.Create(
            instance,
            GameMode.Awdp,
            template,
            "{}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Compose_definition_creates_only_a_compose_claim()
    {
        var instance = CreateInstance(RuntimeKind.Compose, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                },
                EgressPolicy: RuntimeEgressPolicy.Isolated),
            Limits: new(268_435_456, 500_000_000, 128));

        var claim = RuntimeClaimFactory.Create(instance, GameMode.Ctf, template, "{}");

        await Assert.That(claim).IsTypeOf<ClaimComposeRuntime>();
        var compose = (ClaimComposeRuntime)claim;
        await Assert.That(compose.Definition.ComposeYaml)
            .IsEqualTo("services:\n  web:\n    image: challenge:v1");
        await Assert.That(compose.Definition.ProjectName)
            .IsEqualTo($"noctf-{instance.Id:N}-{instance.Generation}");
        await Assert.That(compose.Definition.EgressPolicy)
            .IsEqualTo(RuntimeEgressPolicy.Isolated);
        await Assert.That(compose.Definition.Labels["noctf.io/job-kind"])
            .IsEqualTo("persistent-runtime");
    }

    [Test]
    public async Task Awd_container_claim_carries_the_internal_checker_target()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("challenge:v1"));
        var configuration = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Checker: new AwdCheckerConfiguration(
                    new RunnerJobConfiguration("checker:v1"))),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var claim = (ClaimContainerRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Awd,
            template,
            configuration);

        await Assert.That(claim.Definition.AwdCheckerTargetBinding)
            .IsEqualTo(new RuntimeInternalEndpointBinding());
        await Assert.That(claim.Definition.InternalPorts).IsNull();
        await Assert.That(claim.Definition.ControlCheckUrlBinding).IsNull();
    }

    [Test]
    public async Task Awd_compose_claim_carries_the_target_service()
    {
        var instance = CreateInstance(RuntimeKind.Compose, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                }));
        var configuration = JsonSerializer.Serialize(
            new AwdChallengeConfiguration(
                AwdChallengeConfiguration.CurrentSchemaVersion,
                Checker: new AwdCheckerConfiguration(
                    new RunnerJobConfiguration("checker:v1"),
                    TargetServiceName: "web")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var claim = (ClaimComposeRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Awd,
            template,
            configuration);

        await Assert.That(claim.Definition.AwdCheckerTargetBinding)
            .IsEqualTo(new RuntimeInternalEndpointBinding("web"));
        await Assert.That(claim.Definition.ControlCheckUrlBinding).IsNull();
    }

    [Test]
    public async Task Koh_container_claim_connects_the_worker_to_the_internal_control_endpoint()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var control = new RuntimeUrlBinding(
            "http://{HOST}:{PORT}/control",
            RuntimeExposure.OwnerOnly,
            ContainerPort: 8080);
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition("challenge:v1"),
            ControlCheckUrlBinding: control);

        var claim = (ClaimContainerRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Koh,
            template,
            "{}");

        await Assert.That(claim.Definition.AllowInternalCallback).IsTrue();
        await Assert.That(claim.Definition.InternalPorts).IsEquivalentTo([8080]);
        await Assert.That(claim.Definition.ControlCheckUrlBinding).IsEqualTo(control);
    }

    [Test]
    public async Task Ctf_per_team_compose_targets_only_configured_services()
    {
        var instance = CreateInstance(RuntimeKind.Compose, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                """
                services:
                  web:
                    image: challenge:v1
                  worker:
                    image: worker:v1
                """,
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(134_217_728, 250_000_000, 64),
                    ["worker"] = new(134_217_728, 250_000_000, 64)
                },
                FlagEnvironmentVariables: new Dictionary<string, string>
                {
                    ["web"] = "CHALLENGE_FLAG"
                }),
            FlagSource: RuntimeFlagSource.PerTeam);

        var claim = (ClaimComposeRuntime)RuntimeClaimFactory.Create(
            instance,
            GameMode.Ctf,
            template,
            "{}",
            "flag{fixed-team}");

        await Assert.That(claim.Definition.ServiceEnvironment).IsNotNull();
        await Assert.That(claim.Definition.ServiceEnvironment!.Keys)
            .IsEquivalentTo(["web"]);
        await Assert.That(claim.Definition.ServiceEnvironment["web"]["CHALLENGE_FLAG"])
            .IsEqualTo("flag{fixed-team}");
    }

    [Test]
    public async Task Ctf_per_team_runtime_rejects_a_missing_fixed_flag()
    {
        var instance = CreateInstance(RuntimeKind.Container, RuntimeProvider.Docker);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "challenge:v1",
                FlagEnvironmentVariableName: "FLAG"),
            FlagSource: RuntimeFlagSource.PerTeam);

        var action = () => RuntimeClaimFactory.Create(instance, GameMode.Ctf, template, "{}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Ova_definition_creates_only_an_ova_claim()
    {
        var instance = CreateInstance(RuntimeKind.OvaVm, RuntimeProvider.Libvirt);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new OvaRuntimeDefinition(
                "file:///var/lib/noctf/challenge.ova",
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            Limits: new(1_073_741_824, 1_000_000_000, 256));

        var claim = RuntimeClaimFactory.Create(instance, GameMode.Ctf, template, "{}");

        await Assert.That(claim).IsTypeOf<ClaimOvaRuntime>();
        var ova = (ClaimOvaRuntime)claim;
        await Assert.That(ova.Definition.OvaSource)
            .IsEqualTo(new Uri("file:///var/lib/noctf/challenge.ova"));
        await Assert.That(ova.Definition.Sha256)
            .IsEqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        await Assert.That(ova.Definition.NetworkName)
            .IsEqualTo($"noctf-{instance.Id:N}-{instance.Generation}");
    }

    [Test]
    public async Task Incompatible_definition_and_provider_is_rejected()
    {
        var instance = CreateInstance(RuntimeKind.Compose, RuntimeProvider.Libvirt);
        var template = new ChallengeRuntimeTemplate(
                        RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                }),
            Limits: new(268_435_456, 500_000_000, 128));

        var action = () => RuntimeClaimFactory.Create(instance, GameMode.Ctf, template, "{}");

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    private static RuntimeInstance CreateInstance(RuntimeKind kind, RuntimeProvider provider) =>
        new()
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            CompetitionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CompetitionChallengeId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            TeamId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Generation = 3,
            RuntimeKind = kind,
            RuntimeProvider = provider,
            RunnerPool = "default",
            State = RuntimeState.Queued,
            ProcessingVersion = 7,
            CreatedAt = DateTimeOffset.Parse("2026-07-26T00:00:00Z")
        };
}
