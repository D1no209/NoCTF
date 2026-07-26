using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class OvaRuntimeHandlerTests
{
    [Test]
    public async Task Provision_persists_multi_vm_receipt_and_expands_guest_url()
    {
        var runtime = new RecordingOvaRuntime();
        var capacity = new RecordingCapacity();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current));
        var message = CreateProvisionMessage();

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisioned>();
        var provisioned = (RuntimeProvisioned)result;
        await Assert.That(provisioned.Provider).IsEqualTo(RuntimeProvider.Libvirt);
        await Assert.That(provisioned.Urls)
            .IsEquivalentTo(["http://10.90.0.2:8080/play"]);
        await Assert.That(provisioned.ParticipantUrlIndexes).IsEquivalentTo([0]);
        await Assert.That(runtime.ImportCount).IsEqualTo(1);
        await Assert.That(runtime.DestroyCount).IsEqualTo(0);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Missing_vm_binding_cleans_up_and_releases_capacity()
    {
        var runtime = new RecordingOvaRuntime();
        var capacity = new RecordingCapacity();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current));
        var original = CreateProvisionMessage();
        var message = original with
        {
            Definition = original.Definition with
            {
                UrlBindings =
                [
                    new(
                        "http://{HOST}:{PORT}",
                        RuntimeExposure.Participants,
                        VmId: "missing",
                        GuestPort: 8080)
                ]
            }
        };

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionFailed>();
        await Assert.That(((RuntimeProvisionFailed)result).FailureCode)
            .IsEqualTo(RuntimeFailureCode.UrlExpansionFailed);
        await Assert.That(runtime.DestroyCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    [Test]
    public async Task Stop_uses_persisted_appliance_receipt_and_is_idempotent()
    {
        var runtime = new RecordingOvaRuntime();
        var capacity = new RecordingCapacity();
        var receipt = runtime.CreateReceipt();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(
                RuntimeProvisionWorkStatus.Current,
                new(
                    RuntimeProvider.Libvirt,
                    System.Text.Json.JsonSerializer.Serialize(receipt))));
        var message = new StopOvaRuntime(
            receipt.OperationId,
            8,
            "default",
            "runner-a");

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopped>();
        await Assert.That(runtime.DestroyCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    private static RuntimeProviderHandler CreateHandler(
        IOvaRuntime runtime,
        IRunnerCapacityGate capacity,
        IRuntimeNodeWorkReader reader)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runner:Pool"] = "default",
                ["Runner:Id"] = "runner-a"
            })
            .Build();
        return new(
            new RecordingProviderCatalog(runtime),
            configuration,
            capacity,
            reader);
    }

    private static ProvisionOvaRuntime CreateProvisionMessage()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new(
            runtimeInstanceId,
            8,
            3,
            "default",
            "runner-a",
            new OvaRuntimeRequest(
                runtimeInstanceId,
                3,
                new Uri("file:///var/lib/noctf/challenge.ova"),
                new string('a', 64),
                $"noctf-{runtimeInstanceId:N}-3",
                new(268_435_456, 2_000_000_000, 256),
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(2),
                [
                    new(
                        "http://{HOST}:{PORT}/play",
                        RuntimeExposure.Participants,
                        VmId: "web",
                        GuestPort: 8080)
                ]));
    }

    private sealed class RecordingOvaRuntime : IOvaRuntime
    {
        public int ImportCount { get; private set; }
        public int DestroyCount { get; private set; }

        public Task<OvaRuntimeReceipt> ImportAsync(
            OvaRuntimeRequest request,
            CancellationToken cancellationToken)
        {
            ImportCount++;
            return Task.FromResult(CreateReceipt(request.OperationId, request.Generation));
        }

        public Task DestroyAsync(
            OvaRuntimeReceipt receipt,
            CancellationToken cancellationToken)
        {
            DestroyCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OvaManagedRuntimeResource>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OvaManagedRuntimeResource>>([]);

        public Task DestroyByIdentityAsync(
            OvaManagedRuntimeResource identity,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public OvaRuntimeReceipt CreateReceipt(
            Guid? operationId = null,
            int generation = 3) =>
            new(
                operationId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RuntimeProvider.Libvirt,
                generation,
                "noctf-network",
                "10.90.0.0/28",
                [
                    new("web", "noctf-web", "10.90.0.2"),
                    new("db", "noctf-db", "10.90.0.3")
                ],
                DateTimeOffset.UtcNow);
    }

    private sealed class RecordingProviderCatalog(IOvaRuntime runtime)
        : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IComposeRuntime Compose(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IOvaRuntime Appliance(RuntimeProvider provider) => runtime;
    }

    private sealed class FixedWorkReader(
        RuntimeProvisionWorkStatus status,
        RuntimeStopWork? stop = null) : IRuntimeNodeWorkReader
    {
        public Task<RuntimeProvisionWorkStatus> ReadProvisionStatusAsync(
            IRuntimeProvisionMessage message,
            CancellationToken cancellationToken) =>
            Task.FromResult(status);

        public Task<RuntimeStopWork?> ReadStopAsync(
            IRuntimeStopMessage message,
            CancellationToken cancellationToken) =>
            Task.FromResult(stop);
    }

    private sealed class RecordingCapacity : IRunnerCapacityGate
    {
        public List<Guid> ReleasedRuntimeIds { get; } = [];

        public Task<RunnerHeartbeatStatus> GetHeartbeatAsync(
            string runnerPool,
            string runnerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(RunnerHeartbeatStatus.Online);

        public Task<RunnerPoolInventory> GetPoolInventoryAsync(
            string runnerPool,
            CancellationToken cancellationToken) =>
            Task.FromResult(new RunnerPoolInventory(
                RunnerPoolInventoryAvailability.Available,
                []));

        public Task<RunnerCapacityClaim> TryClaimAsync(
            RunnerCapacityRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerCapacityClaim> TryClaimForRunnerAsync(
            RunnerCapacityRequest request,
            string runnerId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RunnerCapacityReleaseOutcome> ReleaseAsync(
            Guid runtimeInstanceId,
            string runnerId,
            CancellationToken cancellationToken)
        {
            ReleasedRuntimeIds.Add(runtimeInstanceId);
            return Task.FromResult(RunnerCapacityReleaseOutcome.Released);
        }
    }
}
