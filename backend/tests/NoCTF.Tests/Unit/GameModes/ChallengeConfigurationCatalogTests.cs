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
    [Arguments(GameMode.Ctf, "{\"schemaVersion\":1,\"points\":{\"initialPoints\":0,\"minimumPoints\":0,\"decayFactor\":1},\"bloodRewards\":[]}")]
    [Arguments(GameMode.Awd, "{\"schemaVersion\":1,\"flagFormat\":\"\"}")]
    [Arguments(GameMode.Awdp, "{\"schemaVersion\":1,\"break\":null,\"fix\":null,\"requireBreakBeforeFix\":false,\"maxBreakAttempts\":0,\"maxFixAttempts\":0}")]
    [Arguments(GameMode.Koh, "{\"schemaVersion\":1,\"agentUrl\":\"file:///secret\"}")]
    [Arguments(GameMode.Penetration, "{\"schemaVersion\":1,\"stages\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"number\":0,\"name\":\"\",\"prerequisiteIds\":[],\"points\":null}]}")]
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
            var json = WithRuntime(configurations.GetDefaultJson(mode), template);
            if (mode == GameMode.Awd)
                json = WithAwdFlagInjection(json);
            var parsed = runtimes.Get(mode, json);

            await Assert.That(parsed).IsNotNull();
            await Assert.That(parsed!.Provider).IsEqualTo(RuntimeProvider.Kubernetes);
            await Assert.That(parsed.Allocation).IsEqualTo(RuntimeAllocation.PerTeam);
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
            ["command"] = new JsonArray("/usr/local/bin/set-flag"),
            ["timeoutSeconds"] = 30
        };
        return root.ToJsonString();
    }
}
