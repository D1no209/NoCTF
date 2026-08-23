using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class ComposeRuntimeHandlerTests
{
    [Test]
    public async Task Provision_persists_receipt_and_expands_compose_urls()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current));
        var message = CreateProvisionMessage();

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisioned>();
        var provisioned = (RuntimeProvisioned)result;
        await Assert.That(provisioned.Provider).IsEqualTo(RuntimeProvider.Docker);
        await Assert.That(provisioned.Urls)
            .IsEquivalentTo(["http://runner.example:32000/play"]);
        await Assert.That(provisioned.ParticipantUrlIndexes).IsEquivalentTo([0]);
        await Assert.That(runtime.UpCount).IsEqualTo(1);
        await Assert.That(runtime.DownCount).IsEqualTo(0);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Invalid_compose_url_cleans_up_and_releases_capacity()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var reconciler = new RecordingResourceReconciler(RuntimeProvider.Docker);
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current),
            reconciler);
        var message = CreateProvisionMessage() with
        {
            Definition = CreateProvisionMessage().Definition with
            {
                UrlBindings =
                [
                    new(
                        "http://{HOST}:{PORT}",
                        RuntimeExposure.Participants,
                        ContainerPort: 8080,
                        ServiceName: "missing")
                ]
            }
        };

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionTerminated>();
        await Assert.That(((RuntimeProvisionTerminated)result).FailureCode)
            .IsEqualTo(RuntimeFailureCode.UrlExpansionFailed);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(
                message.RuntimeInstanceId,
                message.Generation)]);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    [Test]
    public async Task Provision_failure_cleanup_exception_is_not_reclassified_or_released()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var reconciler = new RecordingResourceReconciler(
            RuntimeProvider.Docker,
            failCleanup: true);
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current),
            reconciler);
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
                        ContainerPort: 8080,
                        ServiceName: "missing")
                ]
            }
        };

        Func<Task> action = () => handler.Handle(message, CancellationToken.None);

        var exception = await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo("cleanup failed");
        await Assert.That(runtime.UpCount).IsEqualTo(1);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(
                message.RuntimeInstanceId,
                message.Generation)]);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Stop_requested_before_provision_releases_capacity_without_starting_provider()
    {
        var runtime = new RecordingComposeRuntime();
        var operations = new List<string>();
        var capacity = new RecordingCapacity(operations);
        var reconciler = new RecordingResourceReconciler(
            RuntimeProvider.Docker,
            operations);
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.StopRequested),
            reconciler);
        var message = CreateProvisionMessage();

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionCanceled>();
        var canceled = (RuntimeProvisionCanceled)result;
        await Assert.That(canceled.RuntimeInstanceId).IsEqualTo(message.RuntimeInstanceId);
        await Assert.That(canceled.Generation).IsEqualTo(message.Generation);
        await Assert.That(canceled.RunnerPool).IsEqualTo(message.RunnerPool);
        await Assert.That(canceled.RunnerId).IsEqualTo(message.RunnerId);
        await Assert.That(runtime.UpCount).IsEqualTo(0);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(
                message.RuntimeInstanceId,
                message.Generation)]);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
        await Assert.That(operations.Count).IsEqualTo(2);
        await Assert.That(operations[0]).IsEqualTo("destroy");
        await Assert.That(operations[1]).IsEqualTo("release");
    }

    [Test]
    public async Task Stop_requested_cleanup_failure_does_not_release_or_acknowledge()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var reconciler = new RecordingResourceReconciler(
            RuntimeProvider.Docker,
            failCleanup: true);
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.StopRequested),
            reconciler);

        Func<Task> action = () => handler.Handle(
            CreateProvisionMessage(),
            CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(runtime.UpCount).IsEqualTo(0);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Stop_requested_capacity_owner_mismatch_fails_closed()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity(
            releaseOutcome: RunnerCapacityReleaseOutcome.OwnerMismatch);
        var reconciler = new RecordingResourceReconciler(RuntimeProvider.Docker);
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.StopRequested),
            reconciler);
        var message = CreateProvisionMessage();

        Func<Task> action = () => handler.Handle(message, CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await Assert.That(runtime.UpCount).IsEqualTo(0);
        await Assert.That(reconciler.Destroyed)
            .IsEquivalentTo([new RuntimeResourceIdentity(
                message.RuntimeInstanceId,
                message.Generation)]);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    [Test]
    public async Task Retained_assignment_does_not_release_running_runtime_capacity()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.AssignmentRetained));
        var message = CreateProvisionMessage();

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeProvisionFailed>();
        await Assert.That(runtime.UpCount).IsEqualTo(0);
        await Assert.That(capacity.ReleasedRuntimeIds).IsEmpty();
    }

    [Test]
    public async Task Stop_uses_the_persisted_compose_receipt()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity();
        var receipt = runtime.CreateReceipt();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(
                RuntimeProvisionWorkStatus.Current,
                new(
                    RuntimeProvider.Docker,
                    System.Text.Json.JsonSerializer.Serialize(receipt),
                    receipt.Generation,
                    RuntimeKind.Compose)));
        var message = new StopComposeRuntime(
            receipt.OperationId,
            3,
            "default",
            "runner-a");

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopped>();
        await Assert.That(runtime.DownCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    [Test]
    public async Task Stop_capacity_owner_mismatch_fails_closed_after_provider_cleanup()
    {
        var runtime = new RecordingComposeRuntime();
        var capacity = new RecordingCapacity(
            releaseOutcome: RunnerCapacityReleaseOutcome.OwnerMismatch);
        var receipt = runtime.CreateReceipt();
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(
                RuntimeProvisionWorkStatus.Current,
                new(
                    RuntimeProvider.Docker,
                    System.Text.Json.JsonSerializer.Serialize(receipt),
                    receipt.Generation,
                    RuntimeKind.Compose)));
        var message = new StopComposeRuntime(
            receipt.OperationId,
            3,
            "default",
            "runner-a");

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopFailed>();
        await Assert.That(result).IsNotTypeOf<RuntimeStopped>();
        await Assert.That(runtime.DownCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    private static RuntimeProviderHandler CreateHandler(
        IComposeRuntime runtime,
        IRunnerCapacityGate capacity,
        IRuntimeNodeWorkReader reader,
        IRuntimeManagedResourceReconciler? reconciler = null)
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
            [reconciler ?? new RecordingResourceReconciler(RuntimeProvider.Docker)],
            configuration.ToRunnerOptions(),
            capacity,
            reader);
    }

    private static ProvisionComposeRuntime CreateProvisionMessage()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new(
            runtimeInstanceId,
            3,
            "default",
            "runner-a",
            new ComposeRequest(
                runtimeInstanceId,
                RuntimeProvider.Docker,
                3,
                "noctf-runtime",
                "services:\n  web:\n    image: challenge:v1",
                new Dictionary<string, string>(),
                new Dictionary<string, string>(),
                new Dictionary<string, RuntimeResourceLimits>
                {
                    ["web"] = new(268_435_456, 500_000_000, 128)
                },
                new(268_435_456, 500_000_000, 128),
                TimeSpan.FromHours(1),
                TimeSpan.FromMinutes(2),
                [
                    new(
                        "http://{HOST}:{PORT}/play",
                        RuntimeExposure.Participants,
                        ContainerPort: 8080,
                        ServiceName: "web")
                ]));
    }

    private sealed class RecordingComposeRuntime : IComposeRuntime
    {
        public int UpCount { get; private set; }
        public int DownCount { get; private set; }

        public Task<ComposeReceipt> UpAsync(
            ComposeRequest request,
            CancellationToken cancellationToken)
        {
            UpCount++;
            return Task.FromResult(CreateReceipt(request.OperationId, request.Generation));
        }

        public Task DownAsync(
            ComposeReceipt receipt,
            CancellationToken cancellationToken)
        {
            DownCount++;
            return Task.CompletedTask;
        }

        public Task<ComposeStatus?> GetStatusAsync(
            ComposeReceipt receipt,
            CancellationToken cancellationToken) => DownCount > 0
                ? Task.FromResult<ComposeStatus?>(new(
                    receipt.ProjectName,
                    RuntimeStatus.Stopped,
                    []))
                : Task.FromResult<ComposeStatus?>(new(
                    receipt.ProjectName,
                    RuntimeStatus.Running,
                    [
                        new(
                            "web",
                            "container-web",
                            RuntimeStatus.Running,
                            new Dictionary<int, int> { [8080] = 32000 },
                            "web")
                    ]));

        public Task<ContainerExecResult> ExecAsync(
            ComposeReceipt receipt,
            string serviceName,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ComposeReceipt CreateReceipt(
            Guid? operationId = null,
            int generation = 3) =>
            new(
                operationId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RuntimeProvider.Docker,
                "noctf-runtime",
                "/tmp/noctf-runtime",
                "runner.example",
                generation,
                DateTimeOffset.UtcNow);
    }

    private sealed class RecordingProviderCatalog(IComposeRuntime runtime)
        : IRuntimeProviderCatalog
    {
        public IContainerLifecycle Containers(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IContainerSandboxLifecycle Sandbox(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IComposeRuntime Compose(RuntimeProvider provider) => runtime;

        public IOvaRuntime Appliance(RuntimeProvider provider) =>
            throw new NotSupportedException();
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

    private sealed class RecordingCapacity(
        List<string>? operations = null,
        RunnerCapacityReleaseOutcome releaseOutcome = RunnerCapacityReleaseOutcome.Released)
        : IRunnerCapacityGate
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
            operations?.Add("release");
            ReleasedRuntimeIds.Add(runtimeInstanceId);
            return Task.FromResult(releaseOutcome);
        }
    }

    private sealed class RecordingResourceReconciler(
        RuntimeProvider provider,
        List<string>? operations = null,
        bool failCleanup = false) : IRuntimeManagedResourceReconciler
    {
        public RuntimeProvider Provider { get; } = provider;
        public List<RuntimeResourceIdentity> Destroyed { get; } = [];

        public Task<IReadOnlyList<RuntimeResourceIdentity>> ListManagedAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RuntimeResourceIdentity>>([]);

        public Task DestroyByIdentityAsync(
            RuntimeResourceIdentity identity,
            CancellationToken cancellationToken)
        {
            operations?.Add("destroy");
            Destroyed.Add(identity);
            return failCleanup
                ? Task.FromException(new InvalidOperationException("cleanup failed"))
                : Task.CompletedTask;
        }
    }
}
