using Microsoft.Extensions.Configuration;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
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
        var handler = CreateHandler(
            runtime,
            capacity,
            new FixedWorkReader(RuntimeProvisionWorkStatus.Current));
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

        await Assert.That(result).IsTypeOf<RuntimeProvisionFailed>();
        await Assert.That(((RuntimeProvisionFailed)result).FailureCode)
            .IsEqualTo(RuntimeFailureCode.UrlExpansionFailed);
        await Assert.That(runtime.DownCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
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
                new(RuntimeProvider.Docker, System.Text.Json.JsonSerializer.Serialize(receipt))));
        var message = new StopComposeRuntime(
            receipt.OperationId,
            8,
            "default",
            "runner-a");

        var result = await handler.Handle(message, CancellationToken.None);

        await Assert.That(result).IsTypeOf<RuntimeStopped>();
        await Assert.That(runtime.DownCount).IsEqualTo(1);
        await Assert.That(capacity.ReleasedRuntimeIds)
            .IsEquivalentTo([message.RuntimeInstanceId]);
    }

    private static RuntimeProviderHandler CreateHandler(
        IComposeRuntime runtime,
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

    private static ProvisionComposeRuntime CreateProvisionMessage()
    {
        var runtimeInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new(
            runtimeInstanceId,
            8,
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
            CancellationToken cancellationToken) =>
            Task.FromResult<ComposeStatus?>(new(
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
