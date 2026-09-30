using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Worker.Runtime;

namespace NoCTF.Tests.Unit.Worker;

public sealed class ChallengeTestRuntimeClaimTests
{
    [Test]
    public async Task Create_TemplateTest_UsesNormalIsolationFlagAndCacheFriendlyLabels()
    {
        var runtimeId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var instance = new TemplateTestRuntimeInstance
        {
            Id = runtimeId,
            ChallengeId = challengeId,
            TestFlagDelivery = RuntimeTestFlagDelivery.Environment,
            TestFlagState = RuntimeTestFlagState.Pending,
            RuntimeKind = RuntimeKind.Container,
            RuntimeProvider = RuntimeProvider.Docker,
            State = RuntimeState.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var template = new ChallengeRuntimeTemplate(
            RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition([new RuntimeServiceDefinition("main", "challenge:test", FlagEnvironmentVariableName: "CHALLENGE_FLAG")]),
            new RuntimeResourceLimits(67_108_864, 100, 64),
            FlagSource: RuntimeFlagSource.PerTeam);

        var message = RuntimeClaimFactory.Create(
            instance,
            "runner-test",
            GameMode.Ctf,
            template,
            null,
            "flag{template-test}");
        var provision = message as ProvisionContainerRuntime
            ?? throw new InvalidOperationException("Expected a Container provision message.");

        await Assert.That(provision.Definition.Services[0].Environment!["CHALLENGE_FLAG"])
            .IsEqualTo("flag{template-test}");
        await Assert.That(provision.Definition.Labels["noctf.io/job-kind"])
            .IsEqualTo("challenge-test-runtime");
        await Assert.That(provision.Definition.Labels["noctf.io/challenge-id"])
            .IsEqualTo(challengeId.ToString("D"));
        await Assert.That(provision.Definition.Labels.ContainsKey("noctf.io/competition-id"))
            .IsFalse();
    }
}
