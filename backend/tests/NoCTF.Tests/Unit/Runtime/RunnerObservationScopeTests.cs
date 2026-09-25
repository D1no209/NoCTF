using k8s;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runtime.Docker.Containers;
using NoCTF.Runtime.Kubernetes.Configuration;
using NSubstitute;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class RunnerObservationScopeTests
{
    [Test]
    public async Task Kubernetes_observation_requests_only_the_attested_workload_nodes()
    {
        var core = Substitute.For<ICoreV1Operations>();
        var client = Substitute.For<IKubernetes>();
        client.CoreV1.Returns(core);
        await using var observer = new RunnerResourceObserver(
            Options.Create(new RunnerOptions { Provider = RuntimeProvider.Kubernetes }),
            new DockerRuntimeOptions(), new KubernetesRuntimeOptions(PodPidsLimit: 512), client,
            new InMemoryClusterLeaseManager(), Substitute.For<IHostApplicationLifetime>(), TimeProvider.System,
            NullLogger<RunnerResourceObserver>.Instance);
        // Missing metrics/permissions must remain closed, after requesting the correct domain.
        var snapshot = await observer.SampleAsync(CancellationToken.None);
        await Assert.That(snapshot.State).IsEqualTo(RunnerAdmissionState.Starting);
        var call = core.ReceivedCalls().Single(call => call.GetMethodInfo().Name == "ListNodeWithHttpMessagesAsync");
        await Assert.That(call.GetArguments()[3]).IsEqualTo("noctf.io/pod-pids-limit=512");
    }
}
