using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Configuration;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public class ChallengeConfigurationCatalogTests
{
    [Test]
    public async Task Defaults_AreValidForEveryGameMode()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var json = catalog.GetDefaultJson(mode);
            var errors = catalog.Validate(mode, json);

            await Assert.That(errors).IsEmpty();
        }
    }

    [Test]
    public async Task CtfDefault_DoesNotPersistFlagInConfigurationJson()
    {
        var json = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Ctf);
        using var document = JsonDocument.Parse(json);

        var containsFlag = document.RootElement.TryGetProperty("flag", out _);

        await Assert.That(containsFlag).IsFalse();
    }

    [Test]
    public async Task Awdp_defaults_put_break_requirement_at_competition_scope()
    {
        using var competition = JsonDocument.Parse(
            GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp));
        using var challenge = JsonDocument.Parse(
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Awdp));

        await Assert.That(
                competition.RootElement.GetProperty("requireBreakBeforeFix").GetBoolean())
            .IsTrue();
        await Assert.That(
                challenge.RootElement.GetProperty("requireBreakBeforeFix").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
    }

    [Test]
    [Arguments(GameMode.Ctf, "{\"schemaVersion\":1,\"points\":{\"initialPoints\":0,\"minimumPoints\":0,\"decayFactor\":1},\"bloodRewards\":[]}")]
    [Arguments(GameMode.Awd, "{\"schemaVersion\":1,\"flagFormat\":\"\"}")]
    [Arguments(GameMode.Awdp, "{\"schemaVersion\":1,\"break\":null,\"fix\":null,\"requireBreakBeforeFix\":false,\"maxBreakSubmissions\":0,\"maxFixSubmissions\":0,\"violationPenalty\":-1}")]
    [Arguments(GameMode.Koh, "{\"schemaVersion\":1,\"pollIntervalSeconds\":0}")]
    public async Task Validate_InvalidModeSpecificConfiguration_ReturnsErrors(GameMode mode, string json)
    {
        var errors = new GameModeChallengeConfigurationCatalog().Validate(mode, json);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task Validate_UnknownSchemaVersion_ReturnsError()
    {
        var errors = new GameModeChallengeConfigurationCatalog().Validate(
            GameMode.Ctf,
            """{"schemaVersion":99,"points":null,"bloodRewards":null}""");

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task CtfChallengeExpression_IsValidatedWhenPointsAreInherited()
    {
        var configuration = new NoCTF.GameModes.Ctf.Configuration.CtfChallengeConfiguration(
            NoCTF.GameModes.Ctf.Configuration.CtfChallengeConfiguration.CurrentSchemaVersion,
            Points: null,
            BloodRewards: null,
            ScoreExpression: "initialPoints = minimumPoints");
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var errors = new GameModeChallengeConfigurationCatalog().Validate(GameMode.Ctf, json);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task CtfChallengeExpression_UsesInheritedPointsAndEligibleTeamBoundary()
    {
        var challenge = new NoCTF.GameModes.Ctf.Configuration.CtfChallengeConfiguration(
            1, Points: null, BloodRewards: null, ScoreExpression: "1m / (solveCount - 2)");
        var competition = new NoCTF.GameModes.Ctf.Configuration.CtfConfiguration(
            1, new(500, 100, 10), []);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var errors = new GameModeChallengeConfigurationCatalog().Validate(
            GameMode.Ctf,
            JsonSerializer.Serialize(challenge, options),
            JsonSerializer.Serialize(competition, options),
            eligibleTeamCount: 2);

        await Assert.That(errors).IsNotEmpty();
    }

    [Test]
    public async Task RuntimeTemplate_ParsesForEveryGameMode()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var runtimes = new ChallengeRuntimeTemplateCatalog();
        var template = new ChallengeRuntimeTemplate(
            RuntimeProvider.Kubernetes,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                ["/app/challenge"],
                new Dictionary<string, string> { ["MODE"] = "competition" },
                new Dictionary<string, string> { ["purpose"] = "challenge" },
                new Dictionary<int, int> { [8080] = 0 },
                new(true, true, true, ["ALL"], [])),
            new(268_435_456, 500_000_000, 128),
            3600);

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var expectedAllocation = mode == GameMode.Koh
                ? RuntimeAllocation.Shared
                : RuntimeAllocation.PerTeam;
            var modeTemplate = template with
            {
                Allocation = expectedAllocation,
                Definition = ((ContainerRuntimeDefinition)template.Definition) with
                {
                    PortMappings = mode == GameMode.Awdp
                        ? new Dictionary<int, int>()
                        : ((ContainerRuntimeDefinition)template.Definition).PortMappings
                }
            };
            var json = WithRuntime(
                configurations.GetDefaultJson(mode),
                modeTemplate);
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);
            var parsed = runtimes.Get(mode, json);

            await Assert.That(parsed).IsNotNull();
            await Assert.That(parsed!.Provider).IsEqualTo(RuntimeProvider.Kubernetes);
            await Assert.That(parsed.Allocation).IsEqualTo(expectedAllocation);
            await Assert.That(parsed.Definition).IsTypeOf<ContainerRuntimeDefinition>();
            await Assert.That(((ContainerRuntimeDefinition)parsed.Definition).Image)
                .IsEqualTo(((ContainerRuntimeDefinition)template.Definition).Image);
            await Assert.That(configurations.Validate(mode, json)).IsEmpty();
        }
    }

    [Test]
    public async Task RuntimeTemplate_InvalidSecurityAndLimits_FailsEveryModeValidator()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var invalid = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition(
                string.Empty,
                Security: new(false, false, false, [], [])),
            Limits: new(0, 0, 0),
            TtlSeconds: -1);

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var errors = configurations.Validate(mode, WithRuntime(configurations.GetDefaultJson(mode), invalid));

            await Assert.That(errors).IsNotEmpty();
            await Assert.That(errors).Contains("Runtime image is required.");
        }
    }

    [Test]
    public async Task Docker_runtime_definitions_reject_InternetOnly_egress()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        ChallengeRuntimeDefinition[] definitions =
        [
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                EgressPolicy: RuntimeEgressPolicy.InternetOnly),
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: registry.example/challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                },
                EgressPolicy: RuntimeEgressPolicy.InternetOnly)
        ];

        foreach (var definition in definitions)
        {
            var template = new ChallengeRuntimeTemplate(
                RuntimeProvider.Docker,
                RuntimeAllocation.PerTeam,
                definition,
                Limits: new(268_435_456, 500_000_000, 128));

            var errors = configurations.Validate(
                GameMode.Ctf,
                WithRuntime(configurations.GetDefaultJson(GameMode.Ctf), template));

            await Assert.That(errors).Contains(
                "Docker runtimes do not support InternetOnly egress; use DenyAll or a Kubernetes Runner Pool.");
        }
    }

    [Test]
    public async Task Kubernetes_runtime_definition_accepts_InternetOnly_egress()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var template = new ChallengeRuntimeTemplate(
            RuntimeProvider.Kubernetes,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                EgressPolicy: RuntimeEgressPolicy.InternetOnly),
            Limits: new(268_435_456, 500_000_000, 0));

        var errors = configurations.Validate(
            GameMode.Ctf,
            WithRuntime(configurations.GetDefaultJson(GameMode.Ctf), template));

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task RuntimeTemplate_ParsesKindSpecificDefinitions()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var runtimes = new ChallengeRuntimeTemplateCatalog();
        ChallengeRuntimeTemplate[] templates =
        [
            new(
                RuntimeProvider.Docker,
                RuntimeAllocation.PerTeam,
                new ContainerRuntimeDefinition("registry.example/challenge:v1")),
            new(
                RuntimeProvider.Docker,
                RuntimeAllocation.PerTeam,
                new ComposeRuntimeDefinition(
                    "services:\n  web:\n    image: registry.example/challenge:v1",
                    new Dictionary<string, RuntimeResourceLimits>
                    {
                        ["web"] = new(268_435_456, 500_000_000, 128)
                    })),
            new(
                RuntimeProvider.Libvirt,
                RuntimeAllocation.PerTeam,
                new OvaRuntimeDefinition(
                    "file:///var/lib/noctf/challenge.ova",
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"))
        ];

        foreach (var template in templates)
        {
            var json = WithRuntime(configurations.GetDefaultJson(GameMode.Ctf), template);
            var parsed = runtimes.Get(GameMode.Ctf, json);

            await Assert.That(parsed).IsNotNull();
            await Assert.That(parsed!.Definition.GetType())
                .IsEqualTo(template.Definition.GetType());
            await Assert.That(parsed.RuntimeKind).IsEqualTo(template.RuntimeKind);
        }
    }

    [Test]
    public async Task RuntimeTemplate_RequiresResourceLimitsForEveryMode()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"));

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var allocation = mode == GameMode.Koh
                ? RuntimeAllocation.Shared
                : RuntimeAllocation.PerTeam;
            var json = WithRuntime(
                configurations.GetDefaultJson(mode),
                runtime with { Allocation = allocation });
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);

            var errors = configurations.Validate(mode, json);

            await Assert.That(errors).Contains("Runtime resource limits are required.");
        }
    }

    [Test]
    public async Task Ova_runtime_requires_a_sha256_digest()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Libvirt,
            RuntimeAllocation.PerTeam,
            new OvaRuntimeDefinition(
                "https://runtime.example/challenge.ova",
                "not-a-digest"),
            Limits: new(1_073_741_824, 1_000_000_000, 256));

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("OvaVm runtimes require a 64-character SHA-256 digest.");
    }

    [Test]
    public async Task Compose_runtime_rejects_unsafe_yaml_during_configuration_validation()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                """
                services:
                  web:
                    image: registry.example/challenge:v1
                    network_mode: host
                """,
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                }),
            Limits: new(268_435_456, 500_000_000, 128));

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Compose service 'web' field 'network_mode' is not supported.");
    }

    [Test]
    public async Task RuntimeTemplate_ExplicitSecurityMustDropAllCapabilities()
    {
        var configurations = new GameModeChallengeConfigurationCatalog();
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                Security: new(true, true, true, [], [])),
            Limits: new(268_435_456, 500_000_000, 128));

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var allocation = mode == GameMode.Koh
                ? RuntimeAllocation.Shared
                : RuntimeAllocation.PerTeam;
            var json = WithRuntime(
                configurations.GetDefaultJson(mode),
                runtime with { Allocation = allocation });
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);

            var errors = configurations.Validate(mode, json);

            await Assert.That(errors)
                .Contains("Runtime security must drop all capabilities.");
        }
    }

    [Test]
    public async Task Container_runtime_rejects_libvirt_provider()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Libvirt,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"));
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Container runtimes require the Docker or Kubernetes provider.");
    }

    [Test]
    public async Task Ctf_runtime_rejects_shared_allocation()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"));
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors).Contains("CTF runtimes must use PerTeam allocation.");
    }

    [Test]
    public async Task Ctf_per_team_container_requires_a_flag_environment_variable()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"),
            FlagSource: RuntimeFlagSource.PerTeam);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("PerTeam Container runtimes require FlagEnvironmentVariableName.");
    }

    [Test]
    public async Task Ctf_per_team_compose_validates_flag_target_services_and_variables()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: registry.example/challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                },
                FlagEnvironmentVariables: new Dictionary<string, string>
                {
                    ["missing"] = "1FLAG",
                    ["web"] = "NOCTF_FLAG"
                }),
            Limits: new(268_435_456, 500_000_000, 128),
            FlagSource: RuntimeFlagSource.PerTeam);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Flag environment target service 'missing' is not defined.");
        await Assert.That(errors).Contains("Flag environment variable '1FLAG' is invalid.");
        await Assert.That(errors)
            .Contains("Flag environment variables cannot use the NOCTF_ prefix.");
    }

    [Test]
    public async Task Ctf_static_runtime_rejects_flag_injection_configuration()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                FlagEnvironmentVariableName: "FLAG"));
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains(
                "FlagEnvironmentVariableName is only supported for PerTeam runtimes.");
    }

    [Test]
    public async Task Ctf_runtime_rejects_awd_rotation_flag_source()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"),
            FlagSource: RuntimeFlagSource.AwdRotation);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("CTF runtimes only support Static or PerTeam flags.");
    }

    [Test]
    public async Task Ctf_runtime_rejects_participant_url_exposure()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            UrlBindings:
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.Participants,
                    ContainerPort: 8080)
            ]);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("CTF runtime URL bindings must use OwnerOnly exposure.");
    }

    [Test]
    public async Task Awd_runtime_rejects_shared_allocation_and_ova()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var shared = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"));
        var ova = new ChallengeRuntimeTemplate(
            RuntimeProvider.Libvirt,
            RuntimeAllocation.PerTeam,
            new OvaRuntimeDefinition(
                "file:///var/lib/noctf/challenge.ova",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

        var sharedErrors = catalog.Validate(
            GameMode.Awd,
            WithAwdFlagInjection(WithRuntime(catalog.GetDefaultJson(GameMode.Awd), shared)));
        var ovaErrors = catalog.Validate(
            GameMode.Awd,
            WithAwdFlagInjection(WithRuntime(catalog.GetDefaultJson(GameMode.Awd), ova)));

        await Assert.That(sharedErrors).Contains("AWD runtimes must use PerTeam allocation.");
        await Assert.That(ovaErrors)
            .Contains("AWD runtimes only support Container or Compose.");
    }

    [Test]
    public async Task Awd_checker_rejects_non_container_provider()
    {
        var checker = new RunnerJobConfiguration(
            RuntimeProvider.Libvirt,
            "registry.example/checker:v1");
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Awd,
            WithChecker(GameMode.Awd, catalog.GetDefaultJson(GameMode.Awd), checker));

        await Assert.That(errors)
            .Contains("AWD Checker requires the Docker or Kubernetes provider.");
    }

    [Test]
    public async Task Awd_checker_requires_the_runtime_provider_and_target_kind()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"),
            Limits: new(268_435_456, 500_000_000, 128));
        var catalog = new GameModeChallengeConfigurationCatalog();
        var challenge = WithAwdFlagInjection(WithRuntime(
            catalog.GetDefaultJson(GameMode.Awd),
            runtime));
        var providerMismatch = WithAwdChecker(
            challenge,
            new AwdCheckerConfiguration(
                new RunnerJobConfiguration(
                    RuntimeProvider.Kubernetes,
                    "registry.example/checker:v1"),
                new ContainerAwdCheckerTarget(
                    "http://{HOST}:{PORT}/health",
                    8080)));
        var kindMismatch = WithAwdChecker(
            challenge,
            new AwdCheckerConfiguration(
                new RunnerJobConfiguration(
                    RuntimeProvider.Docker,
                    "registry.example/checker:v1"),
                new ComposeAwdCheckerTarget(
                    "http://{HOST}:{PORT}/health",
                    "web",
                    8080)));

        await Assert.That(catalog.Validate(GameMode.Awd, providerMismatch))
            .Contains("AWD Runtime and Checker must use the same provider.");
        await Assert.That(catalog.Validate(GameMode.Awd, kindMismatch))
            .Contains("Checker.Target kind must match the AWD Runtime kind.");
    }

    [Test]
    public async Task Awd_checker_null_target_returns_a_validation_error()
    {
        var root = JsonNode.Parse(
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Awd))!
            .AsObject();
        root["checker"] = new JsonObject
        {
            ["job"] = new JsonObject
            {
                ["provider"] = 0,
                ["image"] = "registry.example/checker:v1",
                ["timeoutSeconds"] = 30
            },
            ["target"] = null
        };

        var errors = new GameModeChallengeConfigurationCatalog().Validate(
            GameMode.Awd,
            root.ToJsonString());

        await Assert.That(errors).Contains("Checker.Target is required.");
    }

    [Test]
    public async Task Awdp_target_rejects_public_ports_and_urls()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/target:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings:
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.OwnerOnly,
                    ContainerPort: 8080)
            ]);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Awdp,
            WithRuntime(catalog.GetDefaultJson(GameMode.Awdp), runtime));

        await Assert.That(errors)
            .Contains("AWDP disposable targets cannot configure public ports or URLs.");
    }

    [Test]
    public async Task Awdp_checker_rejects_runner_reserved_environment_variables()
    {
        var checker = new RunnerJobConfiguration(
            RuntimeProvider.Docker,
            "registry.example/checker:v1",
            Environment: new Dictionary<string, string>
            {
                ["TARGET_HOST"] = "attacker-controlled",
                ["NOCTF_CALLBACK_TOKEN"] = "attacker-controlled"
            });
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Awdp,
            WithChecker(GameMode.Awdp, catalog.GetDefaultJson(GameMode.Awdp), checker));

        await Assert.That(errors)
            .Contains("AWDP Checker environment cannot configure Runner-reserved variables.");
    }

    [Test]
    public async Task Checker_environment_rejects_invalid_names_and_noctf_prefix()
    {
        var checker = new RunnerJobConfiguration(
            RuntimeProvider.Docker,
            "registry.example/checker:v1",
            Environment: new Dictionary<string, string>
            {
                ["1INVALID"] = "value",
                ["noctf_callback_url"] = "https://example.invalid"
            });
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in new[] { GameMode.Awd, GameMode.Awdp })
        {
            var errors = catalog.Validate(
                mode,
                WithChecker(mode, catalog.GetDefaultJson(mode), checker));

            await Assert.That(errors)
                .Contains("Checker environment variable '1INVALID' is invalid.");
            await Assert.That(errors)
                .Contains("Checker environment variables cannot use the NOCTF_ prefix.");
        }
    }

    [Test]
    public async Task Checker_null_image_returns_validation_error()
    {
        var checker = new RunnerJobConfiguration(
            RuntimeProvider.Docker,
            null!);
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in new[] { GameMode.Awd, GameMode.Awdp })
        {
            var errors = catalog.Validate(
                mode,
                WithChecker(mode, catalog.GetDefaultJson(mode), checker));

            await Assert.That(errors).Contains("Checker.Image is required.");
        }
    }

    [Test]
    public async Task Runtime_environment_rejects_invalid_names_and_reserved_prefix()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                Environment: new Dictionary<string, string>
                {
                    ["1INVALID"] = "value",
                    ["noctf_callback_url"] = "https://example.invalid"
                }));
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors).Contains("Runtime environment variable '1INVALID' is invalid.");
        await Assert.That(errors)
            .Contains("Runtime environment variables cannot use the NOCTF_ prefix.");
    }

    [Test]
    public async Task Runtime_url_bindings_require_kind_specific_locator_fields()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var container = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            UrlBindings:
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.OwnerOnly,
                    ContainerPort: 8080,
                    ServiceName: "web")
            ]);
        var compose = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                "services:\n  web:\n    image: registry.example/challenge:v1",
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                }),
            UrlBindings:
            [
                new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, ContainerPort: 8080)
            ]);
        var ova = new ChallengeRuntimeTemplate(
            RuntimeProvider.Libvirt,
            RuntimeAllocation.PerTeam,
            new OvaRuntimeDefinition(
                "file:///var/lib/noctf/challenge.ova",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
            UrlBindings:
            [
                new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, VmId: "web")
            ]);

        var containerErrors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), container));
        var composeErrors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), compose));
        var ovaErrors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), ova));

        await Assert.That(containerErrors)
            .Contains("Container URL bindings cannot specify ServiceName, VmId, or GuestPort.");
        await Assert.That(composeErrors)
            .Contains("Compose URL bindings require ServiceName and ContainerPort.");
        await Assert.That(ovaErrors)
            .Contains("OVA URL bindings cannot use PORT without GuestPort.");
    }

    [Test]
    public async Task Runtime_url_binding_must_expand_to_absolute_uri()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            UrlBindings:
            [
                new(
                    "relative/{HOST}/{PORT}",
                    RuntimeExposure.OwnerOnly,
                    ContainerPort: 8080)
            ]);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Runtime URL bindings must expand to an absolute URI.");
    }

    [Test]
    public async Task Runtime_url_binding_null_template_returns_validation_error()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings:
            [
                new(
                    null!,
                    RuntimeExposure.OwnerOnly,
                    ContainerPort: 8080)
            ]);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Runtime URL bindings require a valid exposure and template.");
    }

    [Test]
    public async Task Runtime_url_bindings_reject_null_entries()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings: [null!]);
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Ctf,
            WithRuntime(catalog.GetDefaultJson(GameMode.Ctf), runtime));

        await Assert.That(errors)
            .Contains("Runtime URL bindings cannot contain null entries.");
    }

    [Test]
    public async Task Control_check_url_binding_is_reserved_for_KoH()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            ControlCheckUrlBinding: new(
                "http://{HOST}:{PORT}/control",
                RuntimeExposure.OwnerOnly,
                ContainerPort: 8080));
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in new[] { GameMode.Ctf, GameMode.Awd, GameMode.Awdp })
        {
            var json = WithRuntime(catalog.GetDefaultJson(mode), runtime);
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);

            var errors = catalog.Validate(mode, json);

            await Assert.That(errors)
                .Contains("ControlCheckUrlBinding is only supported for KoH runtimes.");
        }
    }

    [Test]
    public async Task Koh_container_control_check_requires_dynamic_port_mapping()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition(
                "registry.example/hill:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 }),
            UrlBindings:
            [
                new(
                    "http://{HOST}:{PORT}",
                    RuntimeExposure.Participants,
                    ContainerPort: 8080)
            ],
            ControlCheckUrlBinding: new(
                "http://{HOST}:{PORT}/control",
                RuntimeExposure.OwnerOnly,
                ContainerPort: 8081));
        var catalog = new GameModeChallengeConfigurationCatalog();

        var errors = catalog.Validate(
            GameMode.Koh,
            WithRuntime(catalog.GetDefaultJson(GameMode.Koh), runtime));

        await Assert.That(errors)
            .Contains("Container URL bindings require a dynamic port mapping.");
    }

    [Test]
    public async Task Koh_start_requires_shared_runtime_and_control_check_binding()
    {
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Koh);
        var challenge = new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Koh);
        var validator = new GameModeCompetitionConfigurationValidator();

        var errors = validator.ValidateForStart(
            GameMode.Koh,
            competition,
            eligibleTeamCount: 1,
            [challenge]);

        await Assert.That(errors)
            .Contains("Runtime is required before a KoH competition can start.");
    }

    [Test]
    public async Task Koh_shared_runtime_with_control_binding_is_valid_for_start()
    {
        var runtime = new ChallengeRuntimeTemplate(
            RuntimeProvider.Docker,
            RuntimeAllocation.Shared,
            new ContainerRuntimeDefinition(
                "registry.example/hill:v1",
                PortMappings: new Dictionary<int, int> { [8080] = 0 },
                Security: new(true, true, true, ["ALL"], [])),
            Limits: new(268_435_456, 500_000_000, 128),
            UrlBindings:
            [
                new("http://{HOST}:{PORT}", RuntimeExposure.Participants, ContainerPort: 8080)
            ],
            ControlCheckUrlBinding: new(
                "http://{HOST}:{PORT}/control",
                RuntimeExposure.OwnerOnly,
                ContainerPort: 8080));
        var challenge = WithRuntime(
            new GameModeChallengeConfigurationCatalog().GetDefaultJson(GameMode.Koh),
            runtime);

        var errors = new GameModeCompetitionConfigurationValidator().ValidateForStart(
            GameMode.Koh,
            GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Koh),
            eligibleTeamCount: 1,
            [challenge]);

        await Assert.That(errors).IsEmpty();
    }

    private static string WithRuntime(string json, ChallengeRuntimeTemplate runtime)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root["runtime"] = JsonSerializer.SerializeToNode(runtime, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return root.ToJsonString();
    }

    private static string WithChecker(
        GameMode mode,
        string json,
        RunnerJobConfiguration checker)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        object configured = mode == GameMode.Awd
            ? new AwdCheckerConfiguration(
                checker,
                new ContainerAwdCheckerTarget(
                    "http://{HOST}:{PORT}/health",
                    8080))
            : checker;
        root["checker"] = JsonSerializer.SerializeToNode(
            configured,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return root.ToJsonString();
    }

    private static string WithAwdChecker(
        string json,
        AwdCheckerConfiguration checker)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root["checker"] = JsonSerializer.SerializeToNode(
            checker,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return root.ToJsonString();
    }

    private static string WithAwdFlagInjection(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        root["flagInjection"] = new JsonObject
        {
            ["command"] = "/usr/local/bin/set-flag '${FLAG}'",
            ["timeoutSeconds"] = 30
        };
        return root.ToJsonString();
    }
}
