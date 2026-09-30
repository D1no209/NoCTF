using NSubstitute;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Runtime;

public sealed class NamedContainerRuntimeTests
{
    [Test]
    public async Task Entry_deduplication_is_scoped_to_each_service()
    {
        var lifecycle = Substitute.For<IContainerLifecycle>();
        var requests = new List<ContainerRequest>();
        lifecycle.EnsureRunningAsync(Arg.Any<ContainerRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<ContainerRequest>()!; requests.Add(request);
            return Task.FromResult(new ContainerReceipt(request.OperationId, request.Provider,
                NamedContainerRuntime.ResourceName(request.OperationId, request.ServiceName!), RuntimeStatus.Running,
                request.PortMappings.ToDictionary(port => port.Key, _ => 32000), "runner", "10.0.0.1", RuntimeInstanceId: request.OperationId));
        });
        var runtime = new RecordingRuntime(lifecycle, Substitute.For<IContainerSandboxLifecycle>());
        var request = Request([new("web", "nginx"), new("admin", "nginx")],
            [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "web"), new("nc {HOST} {PORT}", RuntimeExposure.OwnerOnly, 80, "web"), new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "admin")]);
        var receipt = await runtime.UpAsync(request, CancellationToken.None);
        await Assert.That(receipt.Services.Count).IsEqualTo(2);
        await Assert.That(requests.All(service => service.PortMappings.Count == 1 && service.PortMappings[80] == 0)).IsTrue();
        await Assert.That(requests.All(service => service.RegisterServiceAlias)).IsTrue();
    }

    [Test]
    public async Task Wsrx_only_has_no_public_mapping_and_single_service_has_no_alias()
    {
        var lifecycle = Substitute.For<IContainerLifecycle>(); ContainerRequest? captured = null;
        lifecycle.EnsureRunningAsync(Arg.Any<ContainerRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            captured = call.Arg<ContainerRequest>()!;
            return Task.FromResult(new ContainerReceipt(captured.OperationId, captured.Provider, "resource", RuntimeStatus.Running,
                new Dictionary<int, int>(), "runner", "10.0.0.1", RuntimeInstanceId: captured.OperationId));
        });
        var runtime = new RecordingRuntime(lifecycle, Substitute.For<IContainerSandboxLifecycle>());
        await runtime.UpAsync(Request([new("main", "nginx")], [new("http://{HOST}:{PORT}", RuntimeExposure.OwnerOnly, 80, "main")])
            with { AccessMode = RuntimeAccessMode.WsrxOnly }, CancellationToken.None);
        await Assert.That(captured!.PortMappings).IsEmpty();
        await Assert.That(captured.InternalPorts!).Contains(80);
        await Assert.That(captured.RegisterServiceAlias).IsFalse();
    }

    [Test]
    public async Task Partial_failure_cleans_all_planned_service_names()
    {
        var lifecycle = Substitute.For<IContainerLifecycle>();
        lifecycle.EnsureRunningAsync(Arg.Any<ContainerRequest>(), Arg.Any<CancellationToken>()).Returns<Task<ContainerReceipt>>(call =>
            call.Arg<ContainerRequest>()!.ServiceName == "broken" ? Task.FromException<ContainerReceipt>(new InvalidOperationException("image rejected"))
                : Task.FromResult(new ContainerReceipt(call.Arg<ContainerRequest>()!.OperationId, RuntimeProvider.Docker, "created", RuntimeStatus.Running, new Dictionary<int, int>(), "runner", "10.0.0.1")));
        lifecycle.GetAsync(Arg.Any<RuntimeProvider>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ContainerReceipt?>(null));
        var runtime = new RecordingRuntime(lifecycle, Substitute.For<IContainerSandboxLifecycle>());
        var request = Request([new("healthy", "nginx"), new("broken", "missing")], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.UpAsync(request, CancellationToken.None));
        await lifecycle.Received(2).DestroyAsync(Arg.Any<ContainerReceipt>(), RuntimeTerminationMode.Force, Arg.Any<RuntimeTerminationPolicy>(), Arg.Any<CancellationToken>());
        await Assert.That(runtime.CleanupCount).IsEqualTo(1);
    }

    private static ContainerRuntimeRequest Request(RuntimeServiceDefinition[] services, RuntimeUrlBinding[] entries) => new(
        Guid.NewGuid(), RuntimeProvider.Docker, services, new Dictionary<string, string>(),
        RuntimeResourceBudgetPolicy.Sum(services.Select(service => service.Resources(256))), null, TimeSpan.FromSeconds(5), entries);

    private sealed class RecordingRuntime(IContainerLifecycle lifecycle, IContainerSandboxLifecycle execution) : NamedContainerRuntime(lifecycle, execution)
    {
        public int CleanupCount { get; private set; }
        protected override RuntimeProvider Provider => RuntimeProvider.Docker;
        protected override string PublicHost => "runner";
        protected override string Namespace => "";
        protected override Task<RuntimeNetworkAttachment> PrepareNetworkAsync(ContainerRuntimeRequest request, CancellationToken cancellationToken) => Task.FromResult(new RuntimeNetworkAttachment("shared"));
        protected override Task RemoveNetworkAsync(ContainerDeploymentReceipt receipt, CancellationToken cancellationToken) { CleanupCount++; return Task.CompletedTask; }
    }
}
