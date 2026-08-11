using Microsoft.Extensions.Configuration;
using System.Net;
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
            [],
            Substitute.For<IRunnerCapacityGate>(),
            outbox,
            configuration);

        await handler.Handle(message, CancellationToken.None);

        var result = outbox.Messages.OfType<AwdpFixResult>().Single();
        await Assert.That(result.RuntimeProcessingVersion).IsEqualTo(11);
        await Assert.That(result.Outcome).IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    [Test]
    public async Task Uncertain_execution_cleans_exact_identity_before_requesting_replay()
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
        var replay = context.Outbox.Messages.OfType<ReplayAwdpFixVerification>().Single();
        await Assert.That(replay.GameplayFactId).IsEqualTo(context.Message.GameplayFactId);
        await Assert.That(replay.PreviousRuntimeInstanceId)
            .IsEqualTo(context.Message.RuntimeInstanceId);
        await Assert.That(replay.RecoveryProcessingVersion).IsEqualTo(12);
    }

    [Test]
    public async Task Cleanup_that_leaves_resources_never_releases_capacity_or_requests_replay()
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

    private static TestContext CreateContext(bool resourcesRemain)
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
        var handler = new AwdpFixVerificationHandler(
            reader,
            new AwdpFixArchiveDownloader(Substitute.For<IHttpClientFactory>()),
            new FixArchivePreparer(configuration),
            Substitute.For<IRuntimeProviderCatalog>(),
            Substitute.For<IAwdpCheckerExecutor>(),
            [reconciler],
            capacity,
            outbox,
            configuration);
        return new(handler, message, reconciler, capacity, outbox);
    }

    private static RunAwdpFixVerification Message() => new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
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

    private sealed record TestContext(
        AwdpFixVerificationHandler Handler,
        RunAwdpFixVerification Message,
        IRuntimeManagedResourceReconciler Reconciler,
        IRunnerCapacityGate Capacity,
        RecordingOutbox Outbox);

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
