using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Gameplay;
using NoCTF.Runner.Composition;
using NoCTF.Runner.Messages;

namespace NoCTF.Tests.Unit.Runner;

public sealed class AwdpFixVerificationCancellationTests
{
    [Test]
    public async Task Non_shutdown_cancellation_publishes_PlatformFailed_with_a_fresh_budget()
    {
        var outbox = new RecordingOutbox();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        var handler = CreateHandler(outbox, lifetime, new OperationCanceledException());
        var message = Message();

        await handler.Handle(message, new CancellationToken(canceled: true));

        var result = outbox.Messages.OfType<AwdpFixResult>().Single();
        await Assert.That(result.GameplayFactId).IsEqualTo(message.GameplayFactId);
        await Assert.That(result.RuntimeInstanceId).IsEqualTo(message.RuntimeInstanceId);
        await Assert.That(result.Outcome).IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    [Test]
    public async Task Host_shutdown_cancellation_escapes_for_durable_redelivery()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();
        var outbox = new RecordingOutbox();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);
        var handler = CreateHandler(outbox, lifetime, new OperationCanceledException());
        Func<Task> action = () => handler.Handle(
            Message(),
            new CancellationToken(canceled: true));

        await Assert.That(action).Throws<OperationCanceledException>();
        await Assert.That(outbox.Messages).IsEmpty();
    }

    [Test]
    public async Task Unexpected_IO_failure_also_publishes_PlatformFailed()
    {
        var outbox = new RecordingOutbox();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        var handler = CreateHandler(outbox, lifetime, new IOException("transport failed"));

        await handler.Handle(Message(), CancellationToken.None);

        await Assert.That(outbox.Messages.OfType<AwdpFixResult>().Single().Outcome)
            .IsEqualTo(AwdpFixOutcome.PlatformFailed);
    }

    private static AwdpFixVerificationHandler CreateHandler(
        RecordingOutbox outbox,
        IHostApplicationLifetime lifetime,
        Exception failure)
    {
        var reader = Substitute.For<IAwdpFixWorkReader>();
        reader.ClaimAsync(
                Arg.Any<RunAwdpFixVerification>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<AwdpFixWorkClaim>(
                failure));
        return new(
            reader,
            new(Substitute.For<IHttpClientFactory>()),
            new(Options.Create(new FixVerificationOptions())),
            Substitute.For<IRuntimeProviderCatalog>(),
            Substitute.For<IAwdpCheckerExecutor>(),
            [],
            Substitute.For<IRunnerCapacityGate>(),
            outbox,
            Options.Create(new RunnerOptions
            {
                Id = "runner-a",
                Pool = "pool-a"
            }),
            lifetime,
            TimeProvider.System,
            NullLogger<AwdpFixVerificationHandler>.Instance);
    }

    private static RunAwdpFixVerification Message() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTimeOffset.UtcNow.AddMinutes(5),
        "runner-a");

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        public List<object> Messages { get; } = [];
        public ValueTask PublishAsync<T>(T message) => Add(message);
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) => Add(message);
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage =>
            Add(message);
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => Add(message);
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;

        private ValueTask Add<T>(T message)
        {
            Messages.Add(message!);
            return ValueTask.CompletedTask;
        }
    }
}
