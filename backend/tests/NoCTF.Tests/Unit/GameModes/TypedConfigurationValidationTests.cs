using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.GameModes;

public sealed class TypedConfigurationValidationTests
{
    [Test]
    [Arguments("nc {host} {port}\nUser: {username}")]
    [Arguments("Connection instructions without a URL or placeholders")]
    public async Task Access_entries_accept_author_text(string template)
    {
        var definition = new CtfChallengeDefinition
        {
            Runtime = new ContainerChallengeRuntimeTemplate
            {
                Allocation = PersistedRuntimeAllocation.PerTeam,
                FlagSource = PersistedRuntimeFlagSource.Static,
                Services = [new ChallengeRuntimeService { Name = "main", Image = "registry.example/challenge:v1" }],
                UrlBindings = [new() { UrlTemplate = template, ServiceName = "main", ContainerPort = 8080 }]
            }
        };

        await Assert.That(new GameModeChallengeConfigurationCatalog()
            .ValidateDefinition(GameMode.Ctf, definition)).IsEmpty();
    }

    [Test]
    [Arguments(GameMode.Ctf)]
    [Arguments(GameMode.Awd)]
    [Arguments(GameMode.Awdp)]
    [Arguments(GameMode.Koh)]
    public async Task Every_mode_has_matching_typed_default_rules_and_definition(GameMode mode)
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var rules = catalog.CreateDefaultRules(mode, Guid.NewGuid());
        var definition = catalog.CreateDefaultDefinition(mode, Guid.NewGuid());

        await Assert.That(rules.Mode).IsEqualTo(mode);
        await Assert.That(definition.Mode).IsEqualTo(mode);
        await Assert.That(catalog.ValidateDefinition(mode, definition)).IsEmpty();
    }

    [Test]
    public async Task Mismatched_leaf_types_are_rejected_without_schema_upgrading()
    {
        var catalog = new GameModeChallengeConfigurationCatalog();
        var errors = catalog.Validate(
            GameMode.Ctf,
            new AwdCompetitionChallengeRules(),
            new CtfCompetitionModeConfiguration(),
            1);

        await Assert.That(errors).Contains(
            "Challenge rules type does not match the competition mode.");
    }

    [Test]
    public async Task Patch_verification_rules_reject_flag_only_overrides()
    {
        var rules = new CtfCompetitionChallengeRules
        {
            MaxFlagAttempts = 5,
            HasFlagTemplate = true
        };
        var definition = new CtfChallengeDefinition
        {
            InteractionKind = CtfInteractionKind.PatchVerification
        };
        var errors = new GameModeChallengeConfigurationCatalog()
            .ValidateRulesForDefinition(
                GameMode.Ctf,
                rules,
                definition,
                CompetitionModeConfigurationDefaults.Create(GameMode.Ctf, Guid.NewGuid()),
                1);

        await Assert.That(errors).Contains(
            "PatchVerification rules cannot configure MaxFlagAttempts.");
        await Assert.That(errors).Contains(
            "PatchVerification rules cannot configure FlagTemplate.");
    }
}
