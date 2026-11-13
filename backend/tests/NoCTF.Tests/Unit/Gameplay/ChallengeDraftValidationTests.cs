using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class ChallengeDraftValidationTests
{
    [Test]
    public async Task Unconfigured_capabilities_can_be_saved_and_copied_but_cannot_execute()
    {
        var definition = new ChallengeDefinition { Capabilities = [new CheckCapabilityDefinition(), new ControlCapabilityDefinition()] };
        var catalog = new GameModeChallengeConfigurationCatalog();
        await Assert.That(catalog.ValidateDraftDefinition(definition)).IsEmpty();
        await Assert.That(catalog.ValidateDefinition(definition).Count).IsGreaterThan(0);
        await Assert.That(ChallengeTemplateCopyPolicy.Supports(definition)).IsTrue();
        var copy = ChallengeTemplateCopyPolicy.Copy(definition, Guid.NewGuid());
        await Assert.That(copy.Capabilities.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Drafts_still_reject_reserved_environment_names()
    {
        var definition = new ChallengeDefinition { Capabilities = [new CheckCapabilityDefinition
        { Environment = [new() { Name = "NOCTF_CALLBACK_TOKEN", Value = "author" }] }] };
        await Assert.That(new GameModeChallengeConfigurationCatalog().ValidateDraftDefinition(definition).Count).IsGreaterThan(0);
    }
}
