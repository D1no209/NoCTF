using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text.Json;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task Checker_job_receives_target_and_claim_bound_callback_only_at_execution_time(bool allowRoot)
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(
            new StubProviderCatalog(runner),
            new StubHttpClientFactory());
        var deadline = DateTimeOffset.Parse("2026-07-24T00:01:00Z");
        var work = new AwdCheckerWork(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("77777777-7777-7777-7777-777777777777"), RuntimeProvider.Docker, RuntimeKind.Container, ContainerReceiptJson(
                Guid.Parse("11111111-1111-1111-1111-111111111111")), "target.internal", "checker:latest", ["/checker"], new Dictionary<string, string> { ["MODE"] = "awd" }, new Uri("https://api.example/api/internal/v1/awd/check-results"), "claim-bound-token", deadline, TimeSpan.FromMinutes(1), "main");

        await executor.ExecuteAsync(work, CancellationToken.None);

        var request = runner.Request!;
        await Assert.That(request.OperationId).IsNotEqualTo(work.RuntimeInstanceId);
        await Assert.That(request.Environment["NOCTF_TARGET_HOST"])
            .IsEqualTo("target.internal");
        await Assert.That(request.Environment["NOCTF_CALLBACK_URL"])
            .IsEqualTo("https://api.example/api/internal/v1/awd/check-results");
        await Assert.That(request.Environment["NOCTF_CALLBACK_TOKEN"])
            .IsEqualTo("claim-bound-token");
        await Assert.That(request.Labels).IsEmpty();
        await Assert.That(request.NetworkPurpose)
            .IsEqualTo(ContainerNetworkPurpose.AwdChecker);
        await Assert.That(request.OperationTimeout).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(runner.Target).IsTypeOf<AttachedContainerRuntimeTarget>();
    }

    [Test]
    public async Task Checker_timeout_cancels_a_hung_provider_wait()
    {
        var runner = new HangingOneShotRunner();
        var executor = new AwdCheckerExecutor(
            new StubProviderCatalog(runner),
            new StubHttpClientFactory());
        var work = new AwdCheckerWork(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("88888888-8888-8888-8888-888888888888"), RuntimeProvider.Docker, RuntimeKind.Container, ContainerReceiptJson(
                Guid.Parse("11111111-1111-1111-1111-111111111111")), "target.internal", "checker:latest", ["/checker"], new Dictionary<string, string>(), new Uri("https://api.example/api/internal/v1/awd/check-results"), "claim-bound-token", DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromMilliseconds(20), "main");

        var outcome = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdCheckerExecutionOutcome.TimedOut);
        await Assert.That(runner.WasCancelled).IsTrue();
    }

    [Test]
    public async Task Checker_supersedes_a_receipt_from_another_runtime()
    {
        var runner = new RecordingOneShotRunner();
        var executor = new AwdCheckerExecutor(
            new StubProviderCatalog(runner),
            new StubHttpClientFactory());
        var runtimeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var work = new AwdCheckerWork(runtimeId, Guid.Parse("99999999-9999-9999-9999-999999999999"), RuntimeProvider.Docker, RuntimeKind.Container, ContainerReceiptJson(Guid.Parse("22222222-2222-2222-2222-222222222222")), "target.internal", "checker:latest", ["/checker"], new Dictionary<string, string>(), new Uri("https://api.example/api/internal/v1/awd/check-results"), "claim-bound-token", DateTimeOffset.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(10), "main");

        var outcome = await executor.ExecuteAsync(work, CancellationToken.None);

        await Assert.That(outcome).IsEqualTo(AwdCheckerExecutionOutcome.Superseded);
        await Assert.That(runner.Request).IsNull();
    }

    private static ContainerRuntimeReceiptData ContainerReceiptJson(Guid runtimeInstanceId) =>
        RuntimeReceiptTestData.From(new ContainerReceipt(
            runtimeInstanceId,
            RuntimeProvider.Docker,
            "target-container",
            RuntimeStatus.Running,
            new Dictionary<int, int>(),
            "localhost",
            "target.internal",
            "target-network",
            runtimeInstanceId));

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

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SuccessHandler());
    }

    private sealed class SuccessHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
