using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration")]
[Category("RunnerAssignmentReconciliation")]
public sealed class RunnerAssignmentReconciliationTests
{
    [Test]
    public async Task Provision_and_stop_messages_route_to_the_assigned_runner_node()
    {
        var runtimeId = Guid.CreateVersion7();
        const string runnerId = "runner-a";
        var definition = new ContainerRequest(runtimeId, RuntimeProvider.Docker, "challenge:latest", [], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<int, int> { [8080] = 0 }, new RuntimeResourceLimits(256 * 1024 * 1024, 250, 128), TimeSpan.FromMinutes(30));

        IRunnerNodeMessage provision = new ProvisionContainerRuntime(runtimeId, runnerId, RuntimeReceiptTestData.Request(definition));
        IRunnerNodeMessage stop = new StopContainerRuntime(runtimeId, runnerId);

        await Assert.That(provision.RunnerId).IsEqualTo(runnerId);
        await Assert.That(stop.RunnerId).IsEqualTo(runnerId);
        await Assert.That(RunnerNodeQueueName.FromRunnerId(provision.RunnerId))
            .IsEqualTo(RunnerNodeQueueName.FromRunnerId(stop.RunnerId));
    }

    [Test]
    public async Task Runtime_uuid_is_the_complete_resource_identity()
    {
        var firstRuntimeId = Guid.CreateVersion7();
        var resetRuntimeId = Guid.CreateVersion7();

        var first = new RuntimeResourceIdentity(firstRuntimeId);
        var reset = new RuntimeResourceIdentity(resetRuntimeId);

        await Assert.That(first).IsNotEqualTo(reset);
        await Assert.That(first.RuntimeInstanceId).IsEqualTo(firstRuntimeId);
        await Assert.That(reset.RuntimeInstanceId).IsEqualTo(resetRuntimeId);
    }

    [Test]
    public async Task Reconciliation_cursor_uses_runtime_uuid_without_generation_state()
    {
        var afterRuntimeId = Guid.CreateVersion7();
        var message = new ReconcileRunnerAssignments(DateTimeOffset.UtcNow, afterRuntimeId);

        await Assert.That(message.AfterRuntimeInstanceId).IsEqualTo(afterRuntimeId);
    }
}
