using NoCTF.Application.Runtime.Ports;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Runtime;

namespace NoCTF.Tests.Unit.Application;

public sealed class AwdProducerTests
{
    [Test]
    public async Task RotateAwdFlags_successful_stdin_injection_activates_claim()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FlagStore(Target(now)) { Claim = new(Guid.NewGuid(), Guid.NewGuid(), "flag{protected}") };
        var injector = new FlagInjector(new(0, false));
        var producer = new RotateAwdFlags(store, new RoundCatalog(), new InjectionCatalog(), injector);

        var result = await producer.ExecuteAsync(now);

        await Assert.That(result.CreatedCount).IsEqualTo(1);
        await Assert.That(store.ActivateCalls).IsEqualTo(1);
        await Assert.That(injector.LastFlag).IsEqualTo("flag{protected}");
    }

    [Test]
    public async Task RotateAwdFlags_failed_injection_never_activates_claim()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FlagStore(Target(now)) { Claim = new(Guid.NewGuid(), Guid.NewGuid(), "flag{protected}") };
        var producer = new RotateAwdFlags(store, new RoundCatalog(), new InjectionCatalog(),
            new FlagInjector(new(2, false)));

        var result = await producer.ExecuteAsync(now);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(store.ActivateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task RotateAwdFlags_timed_out_injection_marks_stopped_runtime_failed()
    {
        var now = DateTimeOffset.UtcNow;
        var target = Target(now);
        var store = new FlagStore(target) { Claim = new(Guid.NewGuid(), Guid.NewGuid(), "flag{protected}") };
        var producer = new RotateAwdFlags(store, new RoundCatalog(), new InjectionCatalog(),
            new FlagInjector(new(-1, true)));

        var result = await producer.ExecuteAsync(now);

        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(store.FailedRuntimeId).IsEqualTo(target.ChallengeInstanceId);
        await Assert.That(store.ActivateCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ProduceAwdChecks_missing_persistent_claim_skips_runner_dispatch()
    {
        var runner = new OneShotRunner();
        var producer = new ProduceAwdChecks(
            new CheckerTargets(TargetChecker(DateTimeOffset.UtcNow)),
            new CheckerConfiguration(),
            new CallbackFactory(),
            new DispatchClaims(null),
            runner);

        var result = await producer.ExecuteAsync(DateTimeOffset.UtcNow);

        await Assert.That(result.CreatedCount).IsEqualTo(0);
        await Assert.That(runner.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ProduceAwdChecks_claimed_round_uses_revision_source_and_completes_claim()
    {
        var now = DateTimeOffset.UtcNow;
        var claim = new ProducerDispatchClaim(Guid.NewGuid(), Guid.NewGuid());
        var claims = new DispatchClaims(claim);
        var runner = new OneShotRunner();
        var producer = new ProduceAwdChecks(
            new CheckerTargets(TargetChecker(now)),
            new CheckerConfiguration(),
            new CallbackFactory(),
            claims,
            runner);

        var result = await producer.ExecuteAsync(now);

        await Assert.That(result.CreatedCount).IsEqualTo(1);
        await Assert.That(runner.Calls).IsEqualTo(1);
        await Assert.That(runner.LastRequest!.OperationTimeout).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(now - claims.StaleBefore!.Value).IsEqualTo(
            TimeSpan.FromSeconds(10) + RunnerScoringCallbackDeliveryPolicy.DispatchLeaseBuffer);
        await Assert.That(runner.LastRequest.Labels["noctf.io/job-kind"]).IsEqualTo("awd-checker");
        await Assert.That(runner.LastRequest.Labels.ContainsKey("noctf.io/expires-at")).IsTrue();
        await Assert.That(claims.OperationKey).Contains(":revision:7:checker");
        await Assert.That(claims.CompletedSucceeded).IsTrue();
    }

    [Test]
    public async Task ProduceAwdChecks_rejected_completion_is_reported_as_failed_dispatch()
    {
        var now = DateTimeOffset.UtcNow;
        var claims = new DispatchClaims(new(Guid.NewGuid(), Guid.NewGuid())) { CompletionAccepted = false };
        var producer = new ProduceAwdChecks(
            new CheckerTargets(TargetChecker(now)),
            new CheckerConfiguration(),
            new CallbackFactory(),
            claims,
            new OneShotRunner());

        var result = await producer.ExecuteAsync(now);

        await Assert.That(result.CreatedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(1);
        await Assert.That(claims.CompletedSucceeded).IsFalse();
    }

    private static AwdFlagRotationTarget Target(DateTimeOffset now) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7, now.AddMinutes(-1), "{}", "{}",
        new(Guid.NewGuid(), RuntimeProvider.Docker, "container", RuntimeStatus.Running,
            new Dictionary<int, int>(), "localhost", "container"));

    private static AwdCheckerTarget TargetChecker(DateTimeOffset now) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-1), "{}", "{}",
        new Uri("http://127.0.0.1:8080"), 7);

    private sealed class RoundCatalog : IAwdRoundConfigurationCatalog
    {
        public AwdRoundSettings Get(string competitionConfigurationJson) => new(60, 10, 2);
    }

    private sealed class InjectionCatalog : IAwdFlagInjectionConfigurationCatalog
    {
        public AwdFlagInjectionSettings? Get(string challengeConfigurationJson) => new(["/usr/local/bin/set-flag"], 10);
    }

    private sealed class FlagStore(AwdFlagRotationTarget target) : IAwdFlagRotationStore
    {
        public AwdFlagInjectionClaim? Claim { get; init; }
        public int ActivateCalls { get; private set; }
        public Guid? FailedRuntimeId { get; private set; }
        public Task<IReadOnlyList<AwdFlagRotationTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AwdFlagRotationTarget>>([target]);
        public Task<AwdFlagInjectionClaim?> TryClaimAsync(
            AwdFlagRotationTarget item, DateTimeOffset validStart, DateTimeOffset validEnd, string flag,
            DateTimeOffset staleBefore, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(Claim);
        public Task<bool> ActivateAsync(
            Guid competitionId, AwdFlagInjectionClaim claim, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ActivateCalls++;
            return Task.FromResult(true);
        }
        public Task<bool> MarkRuntimeFailedAsync(
            Guid competitionId, Guid challengeInstanceId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            FailedRuntimeId = challengeInstanceId;
            return Task.FromResult(true);
        }
    }

    private sealed class FlagInjector(ContainerExecResult result) : IAwdFlagInjector
    {
        public string? LastFlag { get; private set; }
        public Task<ContainerExecResult> InjectAsync(
            ContainerReceipt runtime, AwdFlagInjectionSettings settings, string flag, CancellationToken cancellationToken)
        {
            LastFlag = flag;
            return Task.FromResult(result);
        }
    }

    private sealed class CheckerTargets(AwdCheckerTarget target) : IAwdCheckerTargetStore
    {
        public Task<IReadOnlyList<AwdCheckerTarget>> ListRunningAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AwdCheckerTarget>>([target]);
    }

    private sealed class CheckerConfiguration : IAwdCheckerConfigurationCatalog
    {
        public AwdCheckerSettings Get(string competitionConfigurationJson, string challengeConfigurationJson) =>
            new(60, new(RuntimeProvider.Docker, "checker:latest", ["/checker"], TimeoutSeconds: 10));
    }

    private sealed class CallbackFactory : IAwdCheckerCallbackFactory
    {
        public RunnerScoringCallback Create(AwdCheckerTarget target, string sourceKey) =>
            new(new Uri("https://api.example.test/internal/check"), "runner", new Dictionary<string, string>
            {
                ["sourceKey"] = sourceKey
            });
    }

    private sealed class DispatchClaims(ProducerDispatchClaim? claim) : IProducerDispatchClaimStore
    {
        public string? OperationKey { get; private set; }
        public DateTimeOffset? StaleBefore { get; private set; }
        public bool? CompletedSucceeded { get; private set; }
        public bool CompletionAccepted { get; init; } = true;
        public Task<ProducerDispatchClaim?> TryBeginAsync(
            Guid competitionId, string operationKey, DateTimeOffset staleBefore, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            OperationKey = operationKey;
            StaleBefore = staleBefore;
            return Task.FromResult(claim);
        }
        public Task<bool> CompleteAsync(
            Guid competitionId, string operationKey, ProducerDispatchClaim completedClaim, bool succeeded,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            CompletedSucceeded = succeeded;
            return Task.FromResult(CompletionAccepted);
        }
    }

    private sealed class OneShotRunner : IOneShotJobRunner
    {
        public int Calls { get; private set; }
        public ContainerRequest? LastRequest { get; private set; }
        public Task<OneShotResult> RunAsync(ContainerRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new OneShotResult("job", 0, string.Empty, string.Empty, now, now));
        }
    }
}
