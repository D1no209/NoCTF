using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Capabilities;

namespace NoCTF.Tests.Unit.Gameplay;

public sealed class ChallengeTemplateCopyTests
{
    [Test]
    public async Task Bank_copies_have_independent_runtime_and_capability_children()
    {
        var source = new ChallengeDefinition { ChallengeId = Guid.NewGuid(),
            Capabilities = [new BreakCapabilityDefinition { HasFlagTemplate = true, FlagTemplate = new() { Header = "flag", BodyTemplate = "[TEAMHASH]" } }],
            Runtime = new ContainerChallengeRuntimeTemplate { Services = [new() { Name = "web", Position = 0, Image = "example/web:test",
                Commands = [new() { Value = "serve" }], Environment = [new() { Name = "VALUE", Value = "original" }],
                InternalPorts = [new() { Port = 80 }] }], UrlBindings = [new() { ServiceName = "web", ContainerPort = 80, UrlTemplate = "http://{HOST}:{PORT}" }] } };
        var id = Guid.NewGuid(); var copy = ChallengeTemplateCopyPolicy.Copy(source, id);
        var runtime = (ContainerChallengeRuntimeTemplate)copy.Runtime!;
        await Assert.That(copy.ChallengeId).IsEqualTo(id);
        await Assert.That(runtime.Services.Single().Commands.Single().ChallengeId).IsEqualTo(id);
        await Assert.That(runtime.Services.Single().Environment.Single().ServiceName).IsEqualTo("web");
        await Assert.That(ChallengeDefinitionStructuralComparer.Equals(copy, ChallengeTemplateCopyPolicy.Copy(source, Guid.NewGuid()))).IsTrue();
        ((ContainerChallengeRuntimeTemplate)source.Runtime).Services[0].Environment[0].Value = "changed";
        source.Break!.FlagTemplate.Header = "changed";
        await Assert.That(runtime.Services[0].Environment[0].Value).IsEqualTo("original");
        await Assert.That(copy.Break!.FlagTemplate.Header).IsEqualTo("flag");
    }

    [Test]
    public async Task Valid_repair_capabilities_are_preserved_and_invalid_targets_are_rejected()
    {
        var source = NoCTF.Tests.Unit.Gameplay.ChallengeCapabilityTests.Repairable();
        await Assert.That(ChallengeTemplateCopyPolicy.Supports(source)).IsTrue();
        var copy = ChallengeTemplateCopyPolicy.Copy(source, Guid.NewGuid());
        await Assert.That(copy.Capabilities.Select(value => value.Kind)).IsEquivalentTo(source.Capabilities.Select(value => value.Kind));
        source.Check!.TimeoutSeconds = 45;
        await Assert.That(copy.Check!.TimeoutSeconds).IsEqualTo(30);
        source.Fix!.TargetServiceName = "missing";
        await Assert.That(ChallengeTemplateCopyPolicy.Supports(source)).IsFalse();
        await Assert.That(() => ChallengeTemplateCopyPolicy.Copy(source, Guid.NewGuid())).Throws<ArgumentException>();
    }
}
