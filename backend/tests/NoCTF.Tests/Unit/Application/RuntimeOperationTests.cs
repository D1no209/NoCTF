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
            Lease = new(Guid.NewGuid(), Guid.NewGuid(), "challenge:1", Guid.NewGuid(), RuntimeStatus.Running, false)
        };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.AlreadyExists).IsTrue();
        await Assert.That(runtime.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Provision_ExistingStartingOperation_DoesNotCreateDuplicateRuntime()
    {
        var operations = new Operations
        {
            Lease = new(Guid.NewGuid(), Guid.NewGuid(), "challenge:1", Guid.NewGuid(), RuntimeStatus.Starting, false)
        };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.AlreadyExists).IsTrue();
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

    [Test]
    public async Task Provision_ClaimLeaseUsesFixedServerMaximumAndRecoveryGrace()
    {
        var operations = new Operations();
        var runtime = new Runtime();

        _ = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command() with { OperationTimeout = TimeSpan.FromSeconds(10) });

        await Assert.That(operations.ClaimedAt - operations.StaleBefore)
            .IsEqualTo(TimeSpan.FromMinutes(5.5));
    }

    [Test]
    public async Task Provision_ProviderReturnsWrongClaimToken_RejectsAndDestroysResource()
    {
        var operations = new Operations();
        var runtime = new Runtime { ReceiptOperationId = Guid.NewGuid() };

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Failure).IsEqualTo(RuntimeProvisionFailure.ReceiptMismatch);
        await Assert.That(runtime.DestroyCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Provision_CompetitionNoLongerRunning_DoesNotCreateRuntime()
    {
        var operations = new Operations { BeginFailure = RuntimeOperationBeginFailure.CompetitionNotRunning };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Failure).IsEqualTo(RuntimeProvisionFailure.CompetitionNotRunning);
        await Assert.That(runtime.CreateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Provision_CompetitionFinishesBeforePersistence_DestroysCreatedRuntime()
    {
        var operations = new Operations { CompleteResult = false };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Failure).IsEqualTo(RuntimeProvisionFailure.PersistenceRejected);
        await Assert.That(runtime.DestroyCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Provision_DestroyCompensationFails_PersistsCleanupReceipt()
    {
        var operations = new Operations { CompleteResult = false };
        var runtime = new Runtime { DestroyThrows = true };

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Failure).IsEqualTo(RuntimeProvisionFailure.PersistenceRejected);
        await Assert.That(operations.FailureContext!.CleanupReceipt).IsNotNull();
        await Assert.That(operations.FailureCancellationToken.CanBeCanceled).IsTrue();
    }

    [Test]
    public async Task Provision_PersistenceThrows_PreservesReceiptForRecoveryWithoutImmediateDestroy()
    {
        var operations = new Operations { CompleteThrows = true };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Failure).IsEqualTo(RuntimeProvisionFailure.PersistenceRejected);
        await Assert.That(runtime.DestroyCalls).IsEqualTo(0);
        await Assert.That(operations.FailureContext!.CleanupReceipt).IsNotNull();
    }

    [Test]
    public async Task Provision_PersistenceResponseLost_ReconcilesCommittedReceipt()
    {
        var reconciled = Receipt(Guid.NewGuid());
        var operations = new Operations { CompleteThrows = true, ReconciledReceipt = reconciled };
        var runtime = new Runtime();

        var result = await new ChallengeRuntimeProvisioner(operations, runtime)
            .ExecuteAsync(Command());

        await Assert.That(result.Status).IsEqualTo(RuntimeStatus.Running);
        await Assert.That(result.Receipt).IsEqualTo(reconciled);
        await Assert.That(result.AlreadyExists).IsTrue();
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
        public bool CompleteResult { get; init; } = true;
        public bool CompleteThrows { get; init; }
        public RuntimeOperationFailureContext? FailureContext { get; private set; }
        public CancellationToken FailureCancellationToken { get; private set; }
        public ContainerReceipt? ReconciledReceipt { get; init; }
        public DateTimeOffset ClaimedAt { get; private set; }
        public DateTimeOffset StaleBefore { get; private set; }

        public RuntimeOperationBeginFailure? BeginFailure { get; init; }

        public Task<RuntimeOperationBeginResult> BeginAsync(
            Guid competitionId, string operationKey, RuntimeOperationKind kind,
            DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ClaimedAt = now;
            StaleBefore = staleBefore;
            return Task.FromResult(
                BeginFailure is null
                    ? new RuntimeOperationBeginResult(
                        Lease ?? new(Guid.NewGuid(), competitionId, operationKey, Guid.NewGuid(), RuntimeStatus.Pending, true))
                    : new RuntimeOperationBeginResult(null, BeginFailure));
        }

        public Task<bool> CompleteAsync(
            RuntimeOperationLease lease, ContainerReceipt receipt, Guid challengeId,
            Guid? teamId, DateTimeOffset? expiresAt, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (CompleteThrows)
                throw new InvalidOperationException("persistence failure");
            Completed = true;
            ExpiresAt = expiresAt;
            return Task.FromResult(CompleteResult);
        }

        public Task<RuntimeOperationFailureResult> FailAsync(RuntimeOperationLease lease, RuntimeOperationFailureContext failure, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Failed = true;
            FailureContext = failure;
            FailureCancellationToken = cancellationToken;
            return Task.FromResult(new RuntimeOperationFailureResult(ReconciledReceipt));
        }
    }

    private sealed class Runtime : IContainerLifecycle
    {
        public bool Throw { get; init; }
        public bool DestroyThrows { get; init; }
        public TimeSpan? Delay { get; init; }
        public int CreateCalls { get; private set; }
        public int DestroyCalls { get; private set; }
        public Guid? ReceiptOperationId { get; init; }

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
                ReceiptOperationId ?? request.OperationId,
                RuntimeProvider.Docker,
                "container-id",
                RuntimeStatus.Running,
                request.PortMappings,
                "localhost",
                null);
        }

        public Task DestroyAsync(ContainerReceipt receipt, CancellationToken cancellationToken)
        {
            DestroyCalls++;
            if (DestroyThrows)
                throw new InvalidOperationException("destroy failure");
            return Task.CompletedTask;
        }
        public Task<ContainerReceipt?> GetAsync(RuntimeProvider provider, string resourceId, CancellationToken cancellationToken) =>
            Task.FromResult<ContainerReceipt?>(null);
    }

    private static ContainerReceipt Receipt(Guid operationId) => new(
        operationId,
        RuntimeProvider.Docker,
        "container-id",
        RuntimeStatus.Running,
        new Dictionary<int, int>(),
        "localhost",
        null);
}
