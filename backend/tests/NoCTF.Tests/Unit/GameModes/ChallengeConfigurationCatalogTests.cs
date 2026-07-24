using System.Text.Json;
using System.Text.Json.Nodes;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
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
            "registry.example/challenge:v1",
            ["/app/challenge"],
            new Dictionary<string, string> { ["MODE"] = "competition" },
            new Dictionary<string, string> { ["purpose"] = "challenge" },
            new Dictionary<int, int> { [8080] = 0 },
            new(268_435_456, 500_000_000, 128),
            new(true, true, true, ["ALL"], []),
            3600);

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var expectedAllocation = mode == GameMode.Koh
                ? RuntimeAllocation.Shared
                : RuntimeAllocation.PerTeam;
            var json = WithRuntime(
                configurations.GetDefaultJson(mode),
                template with { Allocation = expectedAllocation });
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);
            var parsed = runtimes.Get(mode, json);

            await Assert.That(parsed).IsNotNull();
            await Assert.That(parsed!.Provider).IsEqualTo(RuntimeProvider.Kubernetes);
            await Assert.That(parsed.Allocation).IsEqualTo(expectedAllocation);
            await Assert.That(parsed.Image).IsEqualTo(template.Image);
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
            string.Empty,
            Limits: new(0, 0, 0),
            Security: new(false, false, false, [], []),
            TtlSeconds: -1);

        foreach (var mode in Enum.GetValues<GameMode>())
        {
            var errors = configurations.Validate(mode, WithRuntime(configurations.GetDefaultJson(mode), invalid));

            await Assert.That(errors).IsNotEmpty();
            await Assert.That(errors).Contains("Runtime image is required.");
        }
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
            "registry.example/hill:v1",
            PortMappings: new Dictionary<int, int> { [8080] = 0 },
            Limits: new(268_435_456, 500_000_000, 128),
            Security: new(true, true, true, ["ALL"], []),
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
