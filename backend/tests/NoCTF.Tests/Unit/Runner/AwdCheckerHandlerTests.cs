using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text.Json;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Ports;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;
using Wolverine.Attributes;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdCheckerHandlerTests
{
    [Test]
    public async Task Checker_provider_io_explicitly_opts_out_of_ambient_ef_transactions()
    {
        var attributes = typeof(AwdCheckerHandler)
            .GetCustomAttributes(typeof(NonTransactionalAttribute), inherit: true);

        await Assert.That(attributes).HasSingleItem();
    }

    [Test]
    public async Task Checker_job_receives_target_and_claim_bound_callback_only_at_execution_time()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner));
        var deadline = DateTimeOffset.Parse("2026-07-24T00:01:00Z");
        var work = new AwdCheckerWork(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            7,
            RuntimeProvider.Docker,
            RuntimeKind.Container,
            3,
            ContainerReceiptJson(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                3),
            null,
            "checker:latest",
            ["/checker"],
            new Dictionary<string, string> { ["MODE"] = "awd" },
            new Uri("http://target.internal/health"),
            new Uri("https://api.example/api/internal/v1/awd/check-results"),
            "claim-bound-token",
            deadline,
            TimeSpan.FromMinutes(1));

        await executor.ExecuteAsync(work, CancellationToken.None);

        var request = runner.Request!;
        await Assert.That(request.OperationId).IsNotEqualTo(work.RuntimeInstanceId);
        await Assert.That(request.Environment["NOCTF_TARGET_URL"])
            .IsEqualTo("http://target.internal/health");
        await Assert.That(request.Environment["NOCTF_CALLBACK_URL"])
            .IsEqualTo("https://api.example/api/internal/v1/awd/check-results");
        await Assert.That(request.Environment["NOCTF_CALLBACK_TOKEN"])
            .IsEqualTo("claim-bound-token");
        await Assert.That(request.Labels["noctf.io/managed"]).IsEqualTo("true");
        await Assert.That(request.Labels["noctf.io/runtime-instance-id"])
            .IsEqualTo(work.RuntimeInstanceId.ToString("D"));
        await Assert.That(request.Labels["noctf.io/purpose"])
            .IsEqualTo("awd-checker");
        await Assert.That(request.OperationTimeout).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(runner.Target).IsTypeOf<AttachedContainerRuntimeTarget>();
    }

    [Test]
    public async Task Checker_timeout_cancels_a_hung_provider_wait()
    {
        var runner = new HangingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner));
        var work = new AwdCheckerWork(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            8,
            RuntimeProvider.Docker,
            RuntimeKind.Container,
            3,
            ContainerReceiptJson(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                3),
            null,
            "checker:latest",
            ["/checker"],
            new Dictionary<string, string>(),
            new Uri("http://target.internal/health"),
            new Uri("https://api.example/api/internal/v1/awd/check-results"),
            "claim-bound-token",
            DateTimeOffset.UtcNow.AddMinutes(1),
            TimeSpan.FromMilliseconds(20));

        var outcome = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdCheckerExecutionOutcome.TimedOut);
        await Assert.That(runner.WasCancelled).IsTrue();
    }

    [Test]
    public async Task Checker_supersedes_a_receipt_from_another_generation()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(new StubProviderCatalog(runner));
        var runtimeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var work = new AwdCheckerWork(
            runtimeId,
            9,
            RuntimeProvider.Docker,
            RuntimeKind.Container,
            4,
            ContainerReceiptJson(runtimeId, 3),
            null,
            "checker:latest",
            ["/checker"],
            new Dictionary<string, string>(),
            new Uri("http://target.internal/health"),
            new Uri("https://api.example/api/internal/v1/awd/check-results"),
            "claim-bound-token",
            DateTimeOffset.UtcNow.AddMinutes(1),
            TimeSpan.FromSeconds(10));

        var outcome = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdCheckerExecutionOutcome.Superseded);
        await Assert.That(runner.Request).IsNull();
    }

    private static string ContainerReceiptJson(Guid runtimeInstanceId, int generation) =>
        JsonSerializer.Serialize(new ContainerReceipt(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "target-container",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            "localhost",
            "target.internal",
            "target-network",
            runtimeInstanceId,
            generation));

    private sealed class StubProviderCatalog(IAttachedOneShotJobRunner runner)
        : IOneShotRuntimeProviderCatalog
    {
        public IOneShotJobRunner OneShot(RuntimeProvider provider) =>
            throw new NotSupportedException();

        public IAttachedOneShotJobRunner Attached(RuntimeProvider provider) => runner;
    }

    private sealed class RecordingOneShotRunner : IAttachedOneShotJobRunner
    {
        public ContainerRequest? Request { get; private set; }
        public AttachedRuntimeTarget? Target { get; private set; }

        public Task<OneShotResult> RunAttachedAsync(
            ContainerRequest request,
            AttachedRuntimeTarget target,
            CancellationToken cancellationToken)
        {
            Request = request;
            Target = target;
            var now = DateTimeOffset.Parse("2026-07-24T00:00:00Z");
            return Task.FromResult(new OneShotResult("checker", 0, "", "", now, now));
        }
    }

    private sealed class HangingOneShotRunner : IAttachedOneShotJobRunner
    {
        public bool WasCancelled { get; private set; }

        public async Task<OneShotResult> RunAttachedAsync(
            ContainerRequest request,
            AttachedRuntimeTarget target,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
            throw new UnreachableException();
        }
    }
}
