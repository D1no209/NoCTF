using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class AwdpCheckerFixInputConfigurationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ParseChallenge_OldSchemaFourJsonWithoutField_DefaultsToFalse()
    {
        const string json = """
            {"schemaVersion":4,"break":null,"fix":null,"requireBreakBeforeFix":null,"maxBreakSubmissions":null,"maxFixSubmissions":null}
            """;

        var configuration = AwdpConfigurationParser.ParseChallenge(json);

        await Assert.That(configuration.SchemaVersion).IsEqualTo(4);
        await Assert.That(configuration.CheckerFixInput).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CheckerFixInput_RoundTripsWithoutChangingSchemaVersion(bool enabled)
    {
        var configuration = new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            Break: null,
            Fix: null,
            RequireBreakBeforeFix: null,
            MaxBreakSubmissions: null,
            MaxFixSubmissions: null,
            CheckerFixInput: enabled);

        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        var roundTripped = AwdpConfigurationParser.ParseChallenge(json);

        await Assert.That(roundTripped.SchemaVersion).IsEqualTo(4);
        await Assert.That(roundTripped.CheckerFixInput).IsEqualTo(enabled);
    }

    [Test]
    public async Task Resolve_OldDefinitionWithoutField_UsesFalse()
    {
        var competitionJson = GameModeDefaultConfiguration.GetCompetitionJson(GameMode.Awdp);
        const string oldDefinition = """
            {"schemaVersion":4,"break":null,"fix":null,"requireBreakBeforeFix":null,"maxBreakSubmissions":null,"maxFixSubmissions":null}
            """;

        var effective = AwdpConfigurationResolver.Resolve(
            competitionJson,
            oldDefinition,
            oldDefinition);

        await Assert.That(effective.CheckerFixInput).IsFalse();
    }

    [Test]
    public async Task Validate_EnabledWithoutChecker_ReturnsExplicitReason()
    {
        var configuration = new AwdpChallengeConfiguration(
            AwdpChallengeConfiguration.CurrentSchemaVersion,
            Break: null,
            Fix: null,
            RequireBreakBeforeFix: null,
            MaxBreakSubmissions: null,
            MaxFixSubmissions: null,
            CheckerFixInput: true);

        var errors = AwdpConfigurationValidator.Validate(configuration);

        await Assert.That(errors).Contains("CheckerFixInput requires Checker.");
    }
}
