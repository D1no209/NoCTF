using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Worker.Runtime;

namespace NoCTF.Tests.Unit.Worker;

public sealed class ChallengeTestRuntimeClaimTests
{
    [Test]
    public async Task Opaque_execution_scope_and_per_team_flag_are_forwarded_to_the_provider_plan()
    {
        var scope = Guid.NewGuid();
        var instance = new PlayerRuntimeInstance { Id = Guid.NewGuid(), ExecutionScopeId = scope, TeamId = Guid.NewGuid(),
            RuntimeKind = RuntimeKind.Container, RuntimeProvider = RuntimeProvider.Docker, AccessMode = RuntimeAccessMode.WsrxOnly };
        var template = new ChallengeRuntimeTemplate(RuntimeAllocation.PerTeam,
            new ContainerRuntimeDefinition([new("web", "challenge:test", FlagEnvironmentVariableName: "FLAG")]),
            new(536_870_912, 500, 256), FlagSource: RuntimeFlagSource.PerTeam);
        var message = (ProvisionContainerRuntime)RuntimeClaimFactory.Create(instance, "runner", GameMode.LiveSolo, template, null, "flag{scoped}");
        await Assert.That(message.Definition.ExecutionScopeId).IsEqualTo(scope);
        await Assert.That(message.Definition.Services[0].Environment!["FLAG"]).IsEqualTo("flag{scoped}");
        await Assert.That(message.Definition.AccessMode).IsEqualTo(RuntimeAccessMode.WsrxOnly);
    }
    [Test]
    public async Task Provider_isolation_evidence_and_scope_survive_relational_receipt_mapping()
    {
        var scope = Guid.NewGuid(); var runtime = Guid.NewGuid();
        var receipt = new ContainerDeploymentReceipt(runtime, RuntimeProvider.Docker, $"noctf-rt-{runtime:N}", "", "localhost",
            DateTimeOffset.UtcNow, [new("web", "resource", RuntimeStatus.Running, new Dictionary<int, int>(), "172.18.0.2")],
            ExecutionScopeId: scope, IsolationState: RuntimeIsolationState.Verified);
        var entity = ContainerRuntimeReceiptData.From(receipt).ToEntity(runtime);
        var restored = ((ContainerRuntimeReceiptData)entity.ToData()).ToReceipt();
        await Assert.That(restored.ExecutionScopeId).IsEqualTo(scope);
        await Assert.That(restored.IsolationState).IsEqualTo(RuntimeIsolationState.Verified);
    }
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
