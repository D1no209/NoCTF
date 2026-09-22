using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class ChallengeConfigurationCatalogTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    public async Task Current_rule_and_definition_defaults_are_valid_for_every_mode()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            await Assert.That(catalog.ValidateRules(
                    mode,
                    catalog.GetDefaultJson(mode),
                    GameModeDefaultConfiguration.GetCompetitionJson(mode),
                    1))
                .IsEmpty();
            await Assert.That(catalog.ValidateDefinition(
                    mode,
                    catalog.GetDefaultDefinitionJson(mode)))
                .IsEmpty();
        }
    }

    [Test]
    public async Task Ctf_accepts_only_current_rules_and_definition_versions()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf);

        await Assert.That(catalog.ValidateRules(
                GameMode.Ctf,
                """{"schemaVersion":2}""",
                competition,
                1))
            .IsEmpty();
        await Assert.That(catalog.ValidateDefinition(
                GameMode.Ctf,
                """{"schemaVersion":3}"""))
            .IsEmpty();
        await Assert.That(catalog.ValidateRules(
                GameMode.Ctf,
                """{"schemaVersion":1}""",
                competition,
                1))
            .Contains("schemaVersion 1 is unsupported; expected 2.");
        await Assert.That(catalog.ValidateDefinition(
                GameMode.Ctf,
                """{"schemaVersion":2}"""))
            .Contains("schemaVersion 2 is unsupported; expected 3.");
    }

    [Test]
    public async Task Rules_and_definition_reject_fields_owned_by_the_other_section()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Ctf);

        await Assert.That(catalog.ValidateRules(
                GameMode.Ctf,
                """{"schemaVersion":2,"runtime":null}""",
                competition,
                1))
            .Contains("RulesJson cannot contain 'runtime' because it belongs to the other challenge section.");
        await Assert.That(catalog.ValidateDefinition(
                GameMode.Ctf,
                """{"schemaVersion":3,"scoreCurve":null}"""))
            .Contains("DefinitionJson cannot contain 'scoreCurve' because it belongs to the other challenge section.");
    }

    [Test]
    public async Task Container_security_is_required_and_must_declare_capability_lists()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var withoutSecurity = CurrentCtfDefinition(new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition("registry.example/challenge:v1"),
            Limits: new(268_435_456, 500_000_000, 128)));
        var nullLists = CurrentCtfDefinition(new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                Security: new(false, false, false, null!, null!)),
            Limits: new(268_435_456, 500_000_000, 128)));

        await Assert.That(catalog.ValidateDefinition(GameMode.Ctf, withoutSecurity))
            .Contains("Runtime security is required.");
        await Assert.That(catalog.ValidateDefinition(GameMode.Ctf, nullLists))
            .Contains("Runtime security must declare capability lists and drop all capabilities.");
    }

    [Test]
    public async Task Explicit_current_container_security_is_valid()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var definition = CurrentCtfDefinition(new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition(
                "registry.example/challenge:v1",
                Security: new(false, false, false, ["ALL"], [])),
            Limits: new(268_435_456, 500_000_000, 128)));

        await Assert.That(catalog.ValidateDefinition(GameMode.Ctf, definition)).IsEmpty();
    }

    [Test]
    public async Task Compose_definition_rejects_author_resource_and_replica_fields()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var definition = CurrentCtfDefinition(new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ComposeRuntimeDefinition(
                """
                services:
                  web:
                    image: registry.example/challenge:v1
                    pids_limit: 128
                    deploy:
                      replicas: 1
                """,
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                }),
            Limits: new(268_435_456, 500_000_000, 128)));

        var errors = catalog.ValidateDefinition(GameMode.Ctf, definition);

        await Assert.That(errors)
            .Contains("Compose service 'web' must declare PID limits through ServiceResources, not pids_limit.");
        await Assert.That(errors)
            .Contains("Compose service 'web' must not declare deploy; resources and replicas are platform-owned.");
    }

    private static string CurrentCtfDefinition(ChallengeRuntimeTemplate runtime)
    {
        var root = JsonNode.Parse(
            new GameModeChallengeConfigurationCatalog().GetDefaultDefinitionJson(GameMode.Ctf))!
            .AsObject();
        root["runtime"] = JsonSerializer.SerializeToNode(runtime, JsonOptions);
        return root.ToJsonString(JsonOptions);
    }
}
