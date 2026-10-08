using NoCTF.Application.LiveSolo.Templates;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.GameModes.Registration;

namespace NoCTF.Tests.Unit.LiveSolo;

public sealed class LiveSoloTemplateCopyTests
{
    [Test]
    public async Task Copies_have_independent_runtime_children_and_per_team_allocation_without_mutating_the_ctf_source()
    {
        var source = new CtfChallengeDefinition { ChallengeId = Guid.NewGuid(), InteractionKind = CtfInteractionKind.FlagSubmission,
            HasFlagTemplate = true, FlagTemplate = new() { Header = "flag", BodyTemplate = "{TEAMHASH}" },
            Runtime = new ContainerChallengeRuntimeTemplate { Allocation = PersistedRuntimeAllocation.Shared, FlagSource = PersistedRuntimeFlagSource.Static,
                Services = [new() { Name = "web", Position = 0, Image = "example/web:test", Commands = [new() { Value = "serve" }],
                    Environment = [new() { Name = "VALUE", Value = "original" }], InternalPorts = [new() { Port = 80 }] }],
                UrlBindings = [new() { ServiceName = "web", ContainerPort = 80, UrlTemplate = "http://{HOST}:{PORT}" }] } };
        var id = Guid.NewGuid();
        var copy = LiveSoloTemplateCopyPolicy.Copy(source, id);
        var runtime = (ContainerChallengeRuntimeTemplate)copy.Runtime!;
        await Assert.That(copy.Mode).IsEqualTo(GameMode.LiveSolo);
        await Assert.That(runtime.Allocation).IsEqualTo(PersistedRuntimeAllocation.PerTeam);
        await Assert.That(source.Runtime.Allocation).IsEqualTo(PersistedRuntimeAllocation.Shared);
        await Assert.That(runtime.Services.Single().Commands.Single().ChallengeId).IsEqualTo(id);
        await Assert.That(runtime.Services.Single().Environment.Single().ServiceName).IsEqualTo("web");
        await Assert.That(ChallengeDefinitionStructuralComparer.Equals(copy, LiveSoloTemplateCopyPolicy.Copy(source, Guid.NewGuid()))).IsTrue();
        ((ContainerChallengeRuntimeTemplate)source.Runtime).Services[0].Environment[0].Value = "changed";
        source.FlagTemplate.Header = "changed";
        await Assert.That(runtime.Services[0].Environment[0].Value).IsEqualTo("original");
        await Assert.That(copy.FlagTemplate.Header).IsEqualTo("flag");
    }
    [Test]
    public async Task Patch_and_checker_sources_are_rejected_instead_of_converted_to_flag_submission()
    {
        var patch = new CtfChallengeDefinition { InteractionKind = CtfInteractionKind.PatchVerification };
        var checker = new CtfChallengeDefinition { Checker = new() { Image = "checker:test" } };
        await Assert.That(LiveSoloTemplateCopyPolicy.Supports(patch)).IsFalse();
        await Assert.That(LiveSoloTemplateCopyPolicy.Supports(checker)).IsFalse();
        await Assert.That(() => LiveSoloTemplateCopyPolicy.Copy(patch, Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(LiveSoloTemplateCopyPolicy.Supports(new AwdChallengeDefinition())).IsFalse();
    }
}
