using NSubstitute;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;
using NoCTF.Domain.Platform;

namespace NoCTF.Tests.Unit.Application;

public sealed class CtfPatchVerificationFeatureGateTests
{
    [Test]
    public async Task Platform_setting_is_disabled_by_default()
    {
        await Assert.That(new PlatformSettings().CtfPatchVerificationEnabled).IsFalse();
    }

    [Test]
    public async Task Disabled_feature_rejects_new_patch_template_before_persistence()
    {
        var store = Substitute.For<IChallengeBankStore>();
        var experiments = Substitute.For<IExperimentalFeatureReader>();
        experiments.IsCtfPatchVerificationEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(false);
        var create = new CreateChallengeTemplate(
            store,
            new GameModeChallengeConfigurationCatalog(),
            experiments);

        var result = await create.ExecuteAsync(new(
            null,
            Guid.NewGuid(),
            GameMode.Ctf,
            ChallengeVisibility.Private,
            "Patch me",
            null,
            "Pwn",
            """{"schemaVersion":3,"interactionKind":1}""",
            DateTimeOffset.UtcNow));

        await Assert.That(result.State)
            .IsEqualTo(ChallengeTemplateWriteState.ExperimentalFeatureDisabled);
        await store.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Test]
    public async Task Disabled_feature_hides_patch_templates_from_non_administrators()
    {
        var store = Substitute.For<IChallengeBankStore>();
        var experiments = Substitute.For<IExperimentalFeatureReader>();
        experiments.IsCtfPatchVerificationEnabledAsync(Arg.Any<CancellationToken>())
            .Returns(false);
        var actorId = Guid.NewGuid();
        store.ListAsync(actorId, false, false, Arg.Any<CancellationToken>())
            .Returns([
                Template("Flag", """{"schemaVersion":2}"""),
                Template("Patch", """{"schemaVersion":3,"interactionKind":1}""")
            ]);

        var result = await new ListChallengeTemplates(store, experiments)
            .ExecuteAsync(actorId, false);

        await Assert.That(result.Select(item => item.Title)).IsEquivalentTo(["Flag"]);
    }

    private static ChallengeTemplateView Template(string title, string definitionJson) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        [],
        GameMode.Ctf,
        ChallengeVisibility.Private,
        title,
        null,
        "Pwn",
        definitionJson,
        null,
        0,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
}
