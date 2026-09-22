using System.Text.Json;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Flags;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class PerTeamFlagGeneratorTests
{
    [Test]
    public async Task Default_template_uses_flag_prefix_and_random_uuid_body()
    {
        var secret = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var competitionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var challengeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var competitionChallengeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var teamId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var flag = PerTeamFlagGenerator.Generate(
            PerTeamFlagTemplate.Default,
            new(secret, competitionId, challengeId, competitionChallengeId, teamId));

        await Assert.That(flag).StartsWith("flag{");
        await Assert.That(flag).EndsWith("}");
        await Assert.That(Guid.TryParse(flag[5..^1], out _)).IsTrue();
    }

    [Test]
    public async Task Default_uuid_body_is_random_for_each_generation()
    {
        var context = new PerTeamFlagContext(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray(),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Guid.Parse("00000001-0000-0000-0000-000000000000"));

        var first = PerTeamFlagGenerator.Generate(PerTeamFlagTemplate.Default, context);
        var replay = PerTeamFlagGenerator.Generate(PerTeamFlagTemplate.Default, context);
        var next = PerTeamFlagGenerator.Generate(
            PerTeamFlagTemplate.Default,
            context with { SpecificationId = Guid.Parse("00000002-0000-0000-0000-000000000000") });

        await Assert.That(replay).IsNotEqualTo(first);
        await Assert.That(next).IsNotEqualTo(first);
    }

    [Test]
    public async Task Missing_header_and_body_are_normalized_to_defaults()
    {
        var flag = PerTeamFlagGenerator.Generate(
            new("  ", string.Empty, false),
            new(new byte[32], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        await Assert.That(flag).StartsWith("flag{");
        await Assert.That(flag).EndsWith("}");
        await Assert.That(Guid.TryParse(flag[5..^1], out _)).IsTrue();
    }

    [Test]
    public async Task Leet_only_changes_literal_text_and_preserves_placeholder_values()
    {
        var teamId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var flag = PerTeamFlagGenerator.Generate(
            new("ctf", "abcdef[TEAMID:N]", true),
            new(
                new byte[32],
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                teamId));

        await Assert.That(flag).StartsWith("ctf{");
        await Assert.That(flag).EndsWith($"{teamId:N}}}");
        await Assert.That(flag[..^33]).IsNotEqualTo("ctf{abcdef");
    }

    [Test]
    public async Task Unknown_placeholder_is_rejected()
    {
        var action = () => PerTeamFlagGenerator.Generate(
            new("", "[UNKNOWN]", false),
            new(new byte[32], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        await Assert.That(action).Throws<FormatException>();
    }

    [Test]
    public async Task Ctf_challenge_template_overrides_competition_dynamic_flag_default()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(500, 100, 10),
            [],
            FlagTemplate: new("competition", "[TEAMHASH]", false));
        var inherited = new CtfChallengeConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            null,
            null);
        var overridden = inherited with
        {
            FlagTemplate = new("challenge", "[TEAMHASH]", false)
        };

        var inheritedTemplate = CtfFlagTemplateResolver.Resolve(
            JsonSerializer.Serialize(competition, options),
            JsonSerializer.Serialize(inherited, options));
        var overriddenTemplate = CtfFlagTemplateResolver.Resolve(
            JsonSerializer.Serialize(competition, options),
            JsonSerializer.Serialize(overridden, options));

        await Assert.That(inheritedTemplate.Header).IsEqualTo("competition");
        await Assert.That(overriddenTemplate.Header).IsEqualTo("challenge");
    }

    [Test]
    public async Task Awd_challenge_template_overrides_competition_dynamic_flag_default()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var competition = AwdConfiguration.Default with
        {
            FlagTemplate = new("competition", "[TEAMHASH]", false)
        };
        var inherited = new AwdChallengeConfiguration(
            AwdChallengeConfiguration.CurrentSchemaVersion);
        var overridden = inherited with
        {
            FlagTemplate = new("challenge", "[TEAMHASH]", false)
        };

        var inheritedTemplate = AwdFlagTemplateResolver.Resolve(
            JsonSerializer.Serialize(competition, options),
            JsonSerializer.Serialize(inherited, options));
        var overriddenTemplate = AwdFlagTemplateResolver.Resolve(
            JsonSerializer.Serialize(competition, options),
            JsonSerializer.Serialize(overridden, options));

        await Assert.That(inheritedTemplate.Header).IsEqualTo("competition");
        await Assert.That(overriddenTemplate.Header).IsEqualTo("challenge");
    }

    [Test]
    public async Task Ctf_rejects_invalid_competition_template_and_accepts_challenge_rule_override()
    {
        var invalidCompetition = new CtfConfiguration(
            CtfConfiguration.CurrentSchemaVersion,
            new(500, 100, 10),
            [],
            FlagTemplate: new("flag", "[UNKNOWN]", false));
        var staticChallenge = new CtfChallengeConfiguration(
            CtfChallengeConfiguration.CurrentSchemaVersion,
            null,
            null,
            FlagTemplate: new("flag", "[TEAMHASH]", false));

        await Assert.That(CtfConfigurationValidator.Validate(invalidCompetition))
            .Contains("FlagTemplate is invalid.");
        await Assert.That(CtfConfigurationValidator.Validate(staticChallenge))
            .IsEmpty();
    }
}
