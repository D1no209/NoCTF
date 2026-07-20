using NoCTF.Application.Runtime;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public class RuntimeOperationTests
{
    [Test]
    public async Task Provision_ExistingRunningOperation_IsIdempotent()
    {
        var operations = new Operations
        {
            Lease = new(Guid.NewGuid(), Guid.NewGuid(), "challenge:1", RuntimeStatus.Running, false)
        };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.AlreadyCompleted).IsTrue();
        await Assert.That(runtime.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Provision_NewOperation_PersistsReceipt()
    {
        var operations = new Operations();
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(operations.Completed).IsTrue();
        await Assert.That(operations.ExpiresAt).IsNotNull();
        await Assert.That(runtime.CreateCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Provision_RuntimeFailure_MarksOperationFailed()
    {
        var operations = new Operations();
        var runtime = new Runtime { Throw = true };

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Status).IsEqualTo(RuntimeStatus.Failed);
        await Assert.That(operations.Failed).IsTrue();
    }

    [Test]
    public async Task Provision_OperationTimeout_MarksOperationFailed()
    {
        var operations = new Operations();
        var runtime = new Runtime { Delay = TimeSpan.FromSeconds(1) };

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command() with { OperationTimeout = TimeSpan.FromMilliseconds(10) });

        await Assert.That(result.Status).IsEqualTo(RuntimeStatus.Failed);
        await Assert.That(operations.Failed).IsTrue();
    }

    private static ProvisionChallengeRuntimeCommand Command() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "challenge:1",
        new(
            Guid.Empty,
            RuntimeProvider.Docker,
            "image:latest",
            [],
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<int, int>(),
            new(64 * 1024 * 1024, 100_000_000, 64),
            new(true, true, true, ["ALL"], []),
            TimeSpan.FromHours(2)));

    private sealed class Operations : IRuntimeOperationStore
    {
        public RuntimeOperationLease? Lease { get; init; }
        public bool Completed { get; private set; }
        public bool Failed { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }

        public Task<RuntimeOperationLease> BeginAsync(
            Guid competitionId, string operationKey, RuntimeOperationKind kind,
            DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(
                Lease ?? new(Guid.NewGuid(), competitionId, operationKey, RuntimeStatus.Pending, true));

        public Task<bool> CompleteAsync(
            RuntimeOperationLease lease, ContainerReceipt receipt, Guid challengeId,
            Guid? teamId, DateTimeOffset? expiresAt, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Completed = true;
            ExpiresAt = expiresAt;
            return Task.FromResult(true);
        }

        public Task FailAsync(RuntimeOperationLease lease, string errorCode, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Failed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Runtime : IContainerLifecycle
    {
        public bool Throw { get; init; }
        public TimeSpan? Delay { get; init; }
        public int CreateCalls { get; private set; }

        public Task<ContainerReceipt> CreateAsync(ContainerRequest request, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (Throw)
                throw new InvalidOperationException("runtime failure");
            return CreateAsyncCore(request, cancellationToken);
        }

        private async Task<ContainerReceipt> CreateAsyncCore(ContainerRequest request, CancellationToken cancellationToken)
        {
            if (Delay is { } delay)
                await Task.Delay(delay, cancellationToken);
            return new ContainerReceipt(
                request.OperationId,
                RuntimeProvider.Docker,
                "container-id",
                RuntimeStatus.Running,
                request.PortMappings,
                "localhost",
                null);
        }

        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken) =>
            Task.FromResult<ContainerReceipt?>(null);
    }
}
