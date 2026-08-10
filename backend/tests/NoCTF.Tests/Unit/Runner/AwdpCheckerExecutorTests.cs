using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpCheckerExecutorTests
{
    [Test]
    public async Task Checker_runs_as_an_isolated_one_shot_on_the_target_network()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));
        var work = new AwdpCheckerWork(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            3,
            RuntimeProvider.Docker,
            "checker:latest",
            ["/checker"],
            new Dictionary<string, string>
            {
                ["SAFE_SETTING"] = "allowed",
                ["TARGET_HOST"] = "must-not-win"
            },
            "network-1",
            "target.internal",
            30,
            new Uri("https://api.example/api/internal/v1/awdp/fix-results"),
            "callback-token",
            TimeSpan.FromSeconds(20));

        await executor.ExecuteAsync(work, CancellationToken.None);

        var request = runner.Request!;
        await Assert.That(request.NetworkName).IsEqualTo("network-1");
        await Assert.That(request.Environment["TARGET_HOST"]).IsEqualTo("target.internal");
        await Assert.That(request.Environment).DoesNotContainKey("TARGET_PORT");
        await Assert.That(request.Environment["TARGET_READY_TIMEOUT_SECONDS"]).IsEqualTo("30");
        await Assert.That(request.Environment["NOCTF_CALLBACK_TOKEN"]).IsEqualTo("callback-token");
        await Assert.That(request.Environment).DoesNotContainKey("OBJECT_KEY");
        await Assert.That(request.Labels).IsEmpty();
        await Assert.That(request.NetworkPurpose)
            .IsEqualTo(ContainerNetworkPurpose.AwdpVerification);
        await Assert.That(request.PortMappings).IsEmpty();
        await Assert.That(request.AllowInternalCallback).IsTrue();
    }

    [Test]
    public async Task Checker_timeout_cancels_provider_without_requesting_wolverine_retry()
    {
        var runner = new RecordingOneShotRunner { WaitForCancellation = true };
        var executor = new AwdpCheckerExecutor(new RecordingCatalog(runner));
        var work = Work() with { Timeout = TimeSpan.FromMilliseconds(50) };

        var outcome = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdpCheckerExecutionOutcome.TimedOut);
        await Assert.That(runner.ObservedCancellation).IsTrue();
    }

    [Test]
    public async Task Completed_checker_waits_for_authenticated_callback_instead_of_racing_it()
    {
        await Assert.That(AwdpCheckerCompletionPolicy.ResultFor(
                AwdpCheckerExecutionOutcome.Completed))
            .IsNull();
        await Assert.That(AwdpCheckerCompletionPolicy.ResultFor(
                AwdpCheckerExecutionOutcome.TimedOut))
            .IsEqualTo(NoCTF.Domain.Gameplay.AwdpFixOutcome.PlatformFailed);
    }

    private static AwdpCheckerWork Work() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        3,
        RuntimeProvider.Docker,
        "checker:latest",
        ["/checker"],
        new Dictionary<string, string>(),
        "network-1",
        "target.internal",
        30,
        new Uri("https://api.example/api/internal/v1/awdp/fix-results"),
        "callback-token",
        TimeSpan.FromSeconds(20));

    private sealed class RecordingCatalog(IOneShotJobRunner runner) : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) => runner;
        public IAttachedOneShotJobRunner Attached(RuntimeProvider provider) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingOneShotRunner : IOneShotJobRunner
    {
        public ContainerRequest? Request { get; private set; }
        public bool WaitForCancellation { get; init; }
        public bool ObservedCancellation { get; private set; }

        public async Task<OneShotResult> RunAsync(
            ContainerRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ObservedCancellation = true;
                    throw;
                }
            }
            var now = DateTimeOffset.UtcNow;
            return new OneShotResult("checker", 0, string.Empty, string.Empty, now, now);
        }
    }
}
