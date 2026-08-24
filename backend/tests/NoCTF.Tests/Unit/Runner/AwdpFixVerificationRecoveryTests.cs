using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpFixVerificationRecoveryTests
{
    [Test]
    public async Task Fix_verification_identity_uses_fact_runtime_and_runner()
    {
        var factId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var patchId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        const string runnerId = "runner-a";

        var message = new RunAwdpFixVerification(
            factId,
            challengeId,
            patchId,
            runtimeId,
            deadline,
            runnerId);
        var fence = new AwdpFixExecutionFenceRequest(
            factId,
            challengeId,
            patchId,
            runtimeId,
            deadline,
            runnerId);

        await Assert.That(message.RuntimeInstanceId).IsEqualTo(fence.RuntimeInstanceId);
        await Assert.That(message.GameplayFactId).IsEqualTo(fence.GameplayFactId);
        await Assert.That(message.RunnerId).IsEqualTo(fence.RunnerId);
    }

    [Test]
    public async Task Recovery_completion_has_no_generation_or_stage_protocol()
    {
        var factId = Guid.CreateVersion7();
        var runtimeId = Guid.CreateVersion7();
        var cleanedAt = DateTimeOffset.UtcNow;
        var completion = new CompleteAwdpFixRecovery(
            factId,
            runtimeId,
            "runner-a",
            cleanedAt);

        await Assert.That(completion.GameplayFactId).IsEqualTo(factId);
        await Assert.That(completion.RuntimeInstanceId).IsEqualTo(runtimeId);
        await Assert.That(completion.CleanedAt).IsEqualTo(cleanedAt);
    }

    [Test]
    public async Task Receipt_cleanup_identity_is_the_runtime_uuid()
    {
        var runtimeId = Guid.CreateVersion7();
        var receipt = new ContainerReceipt(
            runtimeId,
            RuntimeProvider.Docker,
            "container-id",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            null,
            null,
            null,
            runtimeId);

        await Assert.That(receipt.OperationId).IsEqualTo(runtimeId);
        await Assert.That(receipt.RuntimeInstanceId).IsEqualTo(runtimeId);
    }
}
