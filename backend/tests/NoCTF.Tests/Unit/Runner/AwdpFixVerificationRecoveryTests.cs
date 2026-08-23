using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpFixVerificationRecoveryTests
{
    [Test]
    public async Task Runner_result_uses_the_durably_claimed_execution_version()
    {
        var message = Message();
        var reader = Substitute.For<IAwdpFixWorkReader>();
        reader.ClaimAsync(message, Arg.Any<CancellationToken>()).Returns(new AwdpFixWorkClaim(
            AwdpFixExecutionFenceDisposition.Execute,
            new(
                11,
                new(
                    new Uri("https://api.example/fix"),
                    "archive-token",
                    "fix.tar.gz",
                    1,
                    new byte[32]),
                new(
                    message.RuntimeInstanceId,
                    RuntimeProvider.Docker,
                    "target",
                    RuntimeStatus.Running,
                    new Dictionary<int, int>(),
                    null,
                    "target",
                    "network",
                    message.RuntimeInstanceId,
                    message.Generation),
                "fix.sh",
                ["/bin/sh", "/noctf/fix/fix.sh"],
                TimeSpan.FromMinutes(1),
                new(
                    message.RuntimeInstanceId,
                    message.Generation,
                    RuntimeProvider.Docker,
                    "checker:latest",
                    [],
                    new Dictionary<string, string>(),
                    "network",
                    "target",
                    30,
                    new Uri("https://api.example/fix-results"),
                    "callback-token",
                    TimeSpan.FromMinutes(1)))));
        var outbox = new RecordingOutbox();
        var configuration = Configuration(message);
        var handler = new AwdpFixVerificationHandler(
            reader,
            new AwdpFixArchiveDownloader(new StaticHttpClientFactory(
                new HttpClient(new NotFoundHandler()))),
            new FixArchivePreparer(configuration),
            Substitute.For<IRuntimeProviderCatalog>(),
            Substitute.For<IAwdpCheckerExecutor>(),
            Substitute.For<IAwdpFixExecutionFence>(),
            [],
            Substitute.For<IRunnerCapacityGate>(),
            outbox,
            configuration.ToRunnerOptions(),
            NullLogger<AwdpFixVerificationHandler>.Instance);

        await handler.Handle(message, CancellationToken.None);

        var result = outbox.Messages.OfType<AwdpFixResult>().Single();
        await Assert.That(result.RuntimeProcessingVersion).IsEqualTo(11);
        await Assert.That(result.Outcome).IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    [Test]
    public async Task Successful_checker_completion_relies_on_callback_without_duplicate_result()
    {
        var message = Message();
        var archive = CreateFixArchive();
        var reader = Substitute.For<IAwdpFixWorkReader>();
        reader.ClaimAsync(message, Arg.Any<CancellationToken>()).Returns(new AwdpFixWorkClaim(
            AwdpFixExecutionFenceDisposition.Execute,
            new(
                11,
                new(
                    new Uri("https://api.example/fix"),
                    "archive-token",
                    "fix.tar.gz",
                    archive.LongLength,
                    SHA256.HashData(archive)),
                new(
                    message.RuntimeInstanceId,
                    RuntimeProvider.Docker,
                    "target",
                    RuntimeStatus.Running,
                    new Dictionary<int, int>(),
                    null,
                    "target",
                    "network",
                    message.RuntimeInstanceId,
                    message.Generation),
                "fix.sh",
                ["/bin/sh", "/noctf/fix/fix.sh"],
                TimeSpan.FromMinutes(1),
                new(
                    message.RuntimeInstanceId,
                    message.Generation,
                    RuntimeProvider.Docker,
                    "checker:latest",
                    [],
                    new Dictionary<string, string>(),
                    "network",
                    "target",
                    30,
                    new Uri("https://api.example/fix-results"),
                    "callback-token",
                    TimeSpan.FromMinutes(1)))));
        var sandbox = Substitute.For<IContainerSandboxLifecycle>();
        sandbox.CopyArchiveAsync(
                Arg.Any<ContainerReceipt>(),
                Arg.Any<Stream>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sandbox.ExecAsync(
                Arg.Any<ContainerReceipt>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(new ContainerExecResult(0, false));
        var providers = Substitute.For<IRuntimeProviderCatalog>();
        providers.Sandbox(RuntimeProvider.Docker).Returns(sandbox);
        var checker = Substitute.For<IAwdpCheckerExecutor>();
        checker.ExecuteAsync(
                Arg.Any<AwdpCheckerWork>(),
                Arg.Any<CancellationToken>())
            .Returns(AwdpCheckerExecutionOutcome.Completed);
        var outbox = new RecordingOutbox();
        var configuration = Configuration(message);
        var executionFence = Substitute.For<IAwdpFixExecutionFence>();
        executionFence.TryAdvanceStageAsync(
                Arg.Any<AwdpFixStageTransitionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new AwdpFixVerificationHandler(
            reader,
            new AwdpFixArchiveDownloader(new StaticHttpClientFactory(
                new HttpClient(new StaticContentHandler(archive)))),
            new FixArchivePreparer(configuration),
            providers,
            checker,
            executionFence,
            [],
            Substitute.For<IRunnerCapacityGate>(),
            outbox,
            configuration.ToRunnerOptions(),
            NullLogger<AwdpFixVerificationHandler>.Instance);

        await handler.Handle(message, CancellationToken.None);

        await checker.Received(1).ExecuteAsync(
            Arg.Any<AwdpCheckerWork>(),
            Arg.Any<CancellationToken>());
        await Assert.That(outbox.Messages.OfType<AwdpFixResult>()).IsEmpty();
    }

    [Test]
    public async Task Uncertain_execution_cleans_resources_before_terminal_completion()
    {
        var context = CreateContext(resourcesRemain: false);

        await context.Handler.Handle(context.Message, CancellationToken.None);

        await context.Reconciler.Received(1).DestroyByIdentityAsync(
            new RuntimeResourceIdentity(
                context.Message.RuntimeInstanceId,
                context.Message.Generation),
            Arg.Any<CancellationToken>());
        await context.Capacity.Received(1).ReleaseAsync(
            context.Message.RuntimeInstanceId,
            context.Message.RunnerId,
            Arg.Any<CancellationToken>());
        var completion = context.Outbox.Messages.OfType<CompleteAwdpFixRecovery>().Single();
        await Assert.That(completion.GameplayFactId).IsEqualTo(context.Message.GameplayFactId);
        await Assert.That(completion.RuntimeInstanceId)
            .IsEqualTo(context.Message.RuntimeInstanceId);
        await Assert.That(completion.RecoveryProcessingVersion).IsEqualTo(12);
    }

    [Test]
    public async Task Recovery_uses_the_receipt_without_identity_discovery()
    {
        var context = CreateContext(resourcesRemain: false, withReceipt: true);

        await context.Handler.Handle(context.Message, CancellationToken.None);

        await context.Container.Received(1).DestroyAsync(
            Arg.Is<ContainerReceipt>(receipt =>
                receipt != null
                && receipt.OperationId == context.Message.RuntimeInstanceId
                && receipt.Generation == context.Message.Generation),
            Arg.Any<CancellationToken>());
        await context.Sandbox.Received(1).DeleteIsolatedNetworkAsync(
            "target-network",
            Arg.Any<CancellationToken>());
        await context.Reconciler.DidNotReceiveWithAnyArgs().DestroyByIdentityAsync(
            default,
            default);
    }

    [Test]
    public async Task Cleanup_that_leaves_resources_never_releases_capacity_or_completes_recovery()
    {
        var context = CreateContext(resourcesRemain: true);
        Func<Task> action = () => context.Handler.Handle(
            context.Message,
            CancellationToken.None);

        await Assert.That(action).Throws<InvalidOperationException>();
        await context.Capacity.DidNotReceiveWithAnyArgs().ReleaseAsync(
            default,
            string.Empty,
            default);
        await Assert.That(context.Outbox.Messages).IsEmpty();
    }

    private static TestContext CreateContext(bool resourcesRemain, bool withReceipt = false)
    {
        var message = Message();
        var reader = Substitute.For<IAwdpFixWorkReader>();
        reader.ClaimAsync(message, Arg.Any<CancellationToken>()).Returns(new AwdpFixWorkClaim(
            AwdpFixExecutionFenceDisposition.Recover,
            Recovery: new(
                message.GameplayFactId,
                message.RuntimeInstanceId,
                message.Generation,
                12,
                RuntimeProvider.Docker,
                withReceipt
                    ? JsonSerializer.Serialize(new ContainerReceipt(
                        message.RuntimeInstanceId,
                        RuntimeProvider.Docker,
                        "target",
                        RuntimeStatus.Running,
                        new Dictionary<int, int>(),
                        null,
                        "target",
                        "target-network",
                        message.RuntimeInstanceId,
                        message.Generation))
                    : null,
                message.RunnerPool,
                message.RunnerId)));
        var identity = new RuntimeResourceIdentity(
            message.RuntimeInstanceId,
            message.Generation);
        var reconciler = Substitute.For<IRuntimeManagedResourceReconciler>();
        reconciler.Provider.Returns(RuntimeProvider.Docker);
        reconciler.ListManagedAsync(Arg.Any<CancellationToken>())
            .Returns(resourcesRemain
                ? new[] { identity }
                : Array.Empty<RuntimeResourceIdentity>());
        var capacity = Substitute.For<IRunnerCapacityGate>();
        capacity.ReleaseAsync(
                message.RuntimeInstanceId,
                message.RunnerId,
                Arg.Any<CancellationToken>())
            .Returns(RunnerCapacityReleaseOutcome.Released);
        var outbox = new RecordingOutbox();
        var configuration = Configuration(message);
        var container = Substitute.For<IContainerLifecycle>();
        var sandbox = Substitute.For<IContainerSandboxLifecycle>();
        var providers = Substitute.For<IRuntimeProviderCatalog>();
        providers.Containers(RuntimeProvider.Docker).Returns(container);
        providers.Sandbox(RuntimeProvider.Docker).Returns(sandbox);
        var handler = new AwdpFixVerificationHandler(
            reader,
            new AwdpFixArchiveDownloader(Substitute.For<IHttpClientFactory>()),
            new FixArchivePreparer(configuration),
            providers,
            Substitute.For<IAwdpCheckerExecutor>(),
            Substitute.For<IAwdpFixExecutionFence>(),
            [reconciler],
            capacity,
            outbox,
            configuration.ToRunnerOptions(),
            NullLogger<AwdpFixVerificationHandler>.Instance);
        return new(handler, message, reconciler, capacity, outbox, container, sandbox);
    }

    private static RunAwdpFixVerification Message() => new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            3,
            10,
            DateTimeOffset.UtcNow.AddMinutes(5),
            "tests",
            "runner-1");

    private static IConfiguration Configuration(RunAwdpFixVerification message) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runner:Pool"] = message.RunnerPool,
                ["Runner:Id"] = message.RunnerId
            })
            .Build();

    private static byte[] CreateFixArchive()
    {
        using var archive = new MemoryStream();
        using (var gzip = new GZipStream(
                   archive,
                   CompressionMode.Compress,
                   leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "fix.sh")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("#!/bin/sh\nexit 0\n"))
            });
        }
        return archive.ToArray();
    }

    private sealed record TestContext(
        AwdpFixVerificationHandler Handler,
        RunAwdpFixVerification Message,
        IRuntimeManagedResourceReconciler Reconciler,
        IRunnerCapacityGate Capacity,
        RecordingOutbox Outbox,
        IContainerLifecycle Container,
        IContainerSandboxLifecycle Sandbox);

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class StaticContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
    }

    private sealed class RecordingOutbox : ITransactionalMessageOutbox
    {
        public List<object> Messages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerPoolAsync<T>(T message)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerPoolAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerPoolMessage =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
    }
}
