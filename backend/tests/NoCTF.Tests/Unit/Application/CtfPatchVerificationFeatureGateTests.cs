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
            new CtfChallengeDefinition
            {
                InteractionKind = CtfInteractionKind.PatchVerification
            },
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
        store.ListPageAsync(Arg.Any<ChallengeTemplateListQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ChallengeTemplateListPage([
                Template("Flag", CtfInteractionKind.FlagSubmission),
                Template("Patch", CtfInteractionKind.PatchVerification)
            ], 2, ["Pwn"]));

        var result = await new ListChallengeTemplates(store, experiments)
            .ExecutePageAsync(new(actorId, false, false, null, null, 0, 10, false));

        await Assert.That(result.Items.Select(item => item.Title)).IsEquivalentTo(["Flag"]);
    }

    private static ChallengeTemplateSummaryView Template(string title, CtfInteractionKind interactionKind) => new(
        Guid.NewGuid(),
        GameMode.Ctf,
        ChallengeVisibility.Private,
        title,
        "Pwn",
        null,
        0,
        DateTimeOffset.UtcNow,
        interactionKind);
}
