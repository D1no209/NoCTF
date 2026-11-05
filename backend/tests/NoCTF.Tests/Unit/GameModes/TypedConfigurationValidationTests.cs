using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class TypedConfigurationValidationTests
{
    [Test, Arguments("nc {host} {port}\nUser: {username}"), Arguments("Connection instructions without a URL or placeholders")]
    public async Task Runtime_access_entries_preserve_author_text(string template)
    {
        var definition = new ChallengeDefinition { Capabilities = [new BreakCapabilityDefinition()],
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "main", Image = "registry.example/challenge:v1",
                InternalPorts = [new() { Port = 8080 }] }], UrlBindings = [new() { UrlTemplate = template, ServiceName = "main", ContainerPort = 8080 }] } };
        await Assert.That(new GameModeChallengeConfigurationCatalog().ValidateDefinition(definition)).IsEmpty();
    }

    [Test, Arguments(GameMode.Ctf), Arguments(GameMode.Awd), Arguments(GameMode.Awdp), Arguments(GameMode.Koh), Arguments(GameMode.LiveSolo)]
    public async Task Each_consumer_has_matching_typed_default_rules(GameMode mode)
    {
        var id = Guid.NewGuid(); var catalog = new GameModeChallengeConfigurationCatalog();
        var rules = catalog.CreateDefaultRules(mode, id);
        await Assert.That(rules.Mode).IsEqualTo(mode);
        await Assert.That(rules.CompetitionChallengeId).IsEqualTo(id);
        await Assert.That(catalog.Validate(mode, rules, CompetitionModeConfigurationDefaults.Create(mode, Guid.NewGuid()), 1)).IsEmpty();
    }

    [Test]
    public async Task Mismatched_rules_are_rejected_at_the_consumer_boundary()
    {
        var errors = new GameModeChallengeConfigurationCatalog().Validate(GameMode.Ctf,
            new AwdCompetitionChallengeRules(), new CtfCompetitionModeConfiguration(), 1);
        await Assert.That(errors).Contains("Challenge rules type does not match the competition mode.");
    }

    [Test]
    public async Task Ctf_requires_break_and_accepts_additional_valid_capabilities()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var definition = NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable();
        var configuration = CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, Guid.NewGuid());
        var rules = new CtfCompetitionChallengeRules { MaxFlagAttempts = 5 };
        await Assert.That(catalog.ValidateRulesForDefinition(GameMode.Ctf, rules, definition, configuration, 1)).IsEmpty();
        definition.Capabilities.RemoveAll(value => value.Kind == ChallengeCapabilityKind.Break);
        await Assert.That(catalog.ValidateRulesForDefinition(GameMode.Ctf, rules, definition, configuration, 1)).Contains("Required capability Break is missing.");
    }
}
