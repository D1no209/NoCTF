using System.Text.Json;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Awdp.Configuration;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class CheckerRootConfigurationTests
{
    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Existing_definitions_default_to_non_root(GameMode mode)
    {
        await Assert.That(Resolve(mode, "{\"schemaVersion\":4}", "{\"schemaVersion\":4}")).IsFalse();
    }

    [Test]
    [Arguments(GameMode.Awd, false)]
    [Arguments(GameMode.Awd, true)]
    [Arguments(GameMode.Awdp, false)]
    [Arguments(GameMode.Awdp, true)]
    public async Task Root_permission_round_trips_and_is_read_only_from_the_template(GameMode mode, bool enabled)
    {
        var definition = JsonSerializer.Serialize(new { schemaVersion = 4, checkerAllowRoot = enabled });
        var rules = JsonSerializer.Serialize(new { schemaVersion = 4, checkerAllowRoot = !enabled });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var roundTrip = mode == GameMode.Awd
            ? JsonSerializer.Serialize(AwdConfigurationParser.ParseChallenge(definition), options)
            : JsonSerializer.Serialize(AwdpConfigurationParser.ParseChallenge(definition), options);

        await Assert.That(Resolve(mode, rules, roundTrip)).IsEqualTo(enabled);
    }

    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Saving_permission_without_a_checker_returns_a_specific_error(GameMode mode)
    {
        var errors = new GameModeChallengeConfigurationCatalog().ValidateDefinition(
            mode, "{\"schemaVersion\":4,\"checkerAllowRoot\":true}");
        await Assert.That(errors).Contains("CheckerAllowRoot requires Checker.");
    }

    [Test]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    public async Task Competition_rules_cannot_set_checker_root_permission(GameMode mode)
    {
        var errors = new GameModeChallengeConfigurationCatalog().ValidateRules(mode,
            "{\"schemaVersion\":4,\"checkerAllowRoot\":true}",
            GameModeDefaultConfiguration.GetCompetitionJson(mode), 1);
        await Assert.That(errors).Contains(
            "RulesJson cannot contain 'checkerAllowRoot' because it belongs to the other challenge section.");
    }

    private static bool Resolve(GameMode mode, string rules, string definition)
    {
        var competition = GameModeDefaultConfiguration.GetCompetitionJson(mode);
        return mode == GameMode.Awd
            ? new AwdCheckerConfigurationCatalog().Get(competition, rules, definition).CheckerAllowRoot
            : AwdpConfigurationResolver.Resolve(competition, rules, definition).CheckerAllowRoot;
    }
}
