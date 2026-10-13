using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Runtime.Capacity;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed partial class AwdCheckerHandlerTests
{
    [Test]
    public async Task Checker_waits_for_a_busy_auxiliary_slot_without_spending_an_execution_retry()
    {
        var gate = Substitute.For<IRunnerCapacityGate>();
        gate.TryClaimForRunnerAsync(Arg.Any<RunnerCapacityRequest>(), "runner", Arg.Any<CancellationToken>())
            .Returns(new RunnerCapacityClaim(RunnerCapacityAvailability.Insufficient, Failure: RunnerAdmissionFailure.StartupConcurrencyLimited),
                new RunnerCapacityClaim(RunnerCapacityAvailability.Claimed, "runner", RunnerCapacityClaimState.Acquired));
        gate.CanCreateWorkloadAsync(Arg.Any<RuntimeWorkloadIdentity>(), Arg.Any<Guid>(), "runner", Arg.Any<CancellationToken>()).Returns(true);
        var reconciler = Substitute.For<IRuntimeManagedResourceReconciler>();
        reconciler.Provider.Returns(RuntimeProvider.Docker);
        reconciler.WorkloadExistsAsync(Arg.Any<RuntimeWorkloadIdentity>(), Arg.Any<CancellationToken>()).Returns(false);
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(gate, Options.Create(new RunnerOptions { Id = "runner", Pool = "test" }),
            [reconciler], mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        var runner = Substitute.For<IAttachedOneShotJobRunner>();
        runner.RunAttachedAsync(Arg.Any<ContainerRequest>(), Arg.Any<AttachedRuntimeTarget>(), Arg.Any<CancellationToken>())
            .Returns(new OneShotResult("checker", 0, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner), new StubHttpClientFactory(), capacity);
        var work = CapacityWork(TimeSpan.FromSeconds(5));

        await Assert.That(await executor.ExecuteAsync(work, CancellationToken.None)).IsEqualTo(AwdCheckerExecutionOutcome.Completed);

        await gate.Received(2).TryClaimForRunnerAsync(Arg.Is<RunnerCapacityRequest>(request => request != null && request.Workload!.Value.OperationId == work.AttemptId),
            "runner", Arg.Any<CancellationToken>());
        await runner.Received(1).RunAttachedAsync(Arg.Is<ContainerRequest>(request => request != null && request.OperationId == work.AttemptId),
            Arg.Any<AttachedRuntimeTarget>(), Arg.Any<CancellationToken>());
        await gate.Received(1).ReleaseWorkloadAsync(Arg.Any<RuntimeWorkloadIdentity>(), "runner", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Checker_capacity_wait_is_bounded_by_the_original_execution_budget()
    {
        var gate = Substitute.For<IRunnerCapacityGate>();
        gate.TryClaimForRunnerAsync(Arg.Any<RunnerCapacityRequest>(), "runner", Arg.Any<CancellationToken>())
            .Returns(new RunnerCapacityClaim(RunnerCapacityAvailability.Insufficient, Failure: RunnerAdmissionFailure.StartupConcurrencyLimited));
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(gate, Options.Create(new RunnerOptions { Id = "runner", Pool = "test" }),
            [], mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner), new StubHttpClientFactory(), capacity);

        await Assert.That(await executor.ExecuteAsync(CapacityWork(TimeSpan.FromMilliseconds(50)), CancellationToken.None))
            .IsEqualTo(AwdCheckerExecutionOutcome.TimedOut);
        await Assert.That(runner.Request).IsNull();
        await gate.DidNotReceive().ReleaseWorkloadAsync(Arg.Any<RuntimeWorkloadIdentity>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Checker_does_not_wait_for_non_concurrency_admission_failures()
    {
        var gate = Substitute.For<IRunnerCapacityGate>();
        gate.TryClaimForRunnerAsync(Arg.Any<RunnerCapacityRequest>(), "runner", Arg.Any<CancellationToken>())
            .Returns(new RunnerCapacityClaim(RunnerCapacityAvailability.Insufficient, Failure: RunnerAdmissionFailure.MemoryActualCapacityInsufficient));
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(gate, Options.Create(new RunnerOptions { Id = "runner", Pool = "test" }),
            [], mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner), new StubHttpClientFactory(), capacity);

        await Assert.That(await executor.ExecuteAsync(CapacityWork(TimeSpan.FromSeconds(5)), CancellationToken.None))
            .IsEqualTo(AwdCheckerExecutionOutcome.AbnormalExit);
        await Assert.That(runner.Request).IsNull();
        await gate.Received(1).TryClaimForRunnerAsync(Arg.Any<RunnerCapacityRequest>(), "runner", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Checker_does_not_start_provider_io_after_its_absolute_deadline()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner), new StubHttpClientFactory());
        var work = CapacityWork(TimeSpan.FromSeconds(5)) with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) };

        await Assert.That(await executor.ExecuteAsync(work, CancellationToken.None)).IsEqualTo(AwdCheckerExecutionOutcome.TimedOut);
        await Assert.That(runner.Request).IsNull();
    }

    [Test]
    public async Task Caller_cancellation_interrupts_capacity_wait_without_running_the_checker()
    {
        var gate = Substitute.For<IRunnerCapacityGate>();
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.TryClaimForRunnerAsync(Arg.Any<RunnerCapacityRequest>(), "runner", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempted.TrySetResult();
                return new RunnerCapacityClaim(RunnerCapacityAvailability.Insufficient, Failure: RunnerAdmissionFailure.StartupConcurrencyLimited);
            });
        using var mutations = new RunnerResourceMutationCoordinator();
        var capacity = new AuxiliaryRuntimeCapacity(gate, Options.Create(new RunnerOptions { Id = "runner", Pool = "test" }),
            [], mutations, NullLogger<AuxiliaryRuntimeCapacity>.Instance);
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner), new StubHttpClientFactory(), capacity);
        using var caller = new CancellationTokenSource();
        var execution = executor.ExecuteAsync(CapacityWork(TimeSpan.FromSeconds(5)), caller.Token);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await caller.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
        await Assert.That(runner.Request).IsNull();
    }

    private static AwdCheckerWork CapacityWork(TimeSpan timeout)
    {
        var runtimeId = Guid.NewGuid();
        return new(runtimeId, Guid.NewGuid(), RuntimeProvider.Docker, RuntimeKind.Container, ContainerReceiptJson(runtimeId),
            "target.internal", "checker:latest", [], new Dictionary<string, string>(),
            new Uri("https://api.example/api/internal/v1/awd/check-results"), "claim-bound-token",
            DateTimeOffset.UtcNow.Add(timeout), timeout, "main", Guid.NewGuid(), [],
            new Uri("https://api.example/api/internal/v1/awd/check-task"), new Dictionary<string, string> { ["main"] = "target.internal" });
    }
}
