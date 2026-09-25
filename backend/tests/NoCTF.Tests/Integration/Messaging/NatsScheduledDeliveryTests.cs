using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Messaging;
using NoCTF.Hosting;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class NatsScheduledDeliveryTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Scheduled_control_message_uses_a_separate_subject_in_the_same_stream(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, assignRandomHostPort: true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await nats.StartAsync(cancellationToken);
            var suffix = Guid.NewGuid().ToString("N");
            var stream = $"NOCTF_TEST_SCHEDULE_{suffix}";
            var consumer = $"noctf-test-schedule-{suffix}";
            var subject = NatsSubjects.Subject(WorkerQueue.Control);
            var probe = new ScheduledProbe();
            var connectionString =
                $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddSingleton(probe))
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(ScheduledProbeHandler));
                    options.UseNats(connectionString)
                        .AutoProvision()
                        .UseJetStream(_ => { })
                        .DefineWorkQueueStream(
                            stream,
                            configuration => configuration.WithSubjects(
                                    subject,
                                    NatsSubjects.ScheduledSubject(WorkerQueue.Control))
                                .EnableScheduledDelivery(),
                            subject);
                    options.ListenToNatsSubject(subject)
                        .UseJetStream(stream, consumer);
                    options.PublishMessage<ScheduledProbeMessage>()
                        .ToNatsSubject(subject);
                })
                .Build();
            await host.StartAsync(cancellationToken);
            try
            {
                var message = new ScheduledProbeMessage(Guid.CreateVersion7());
                await host.Services.GetRequiredService<IMessageBus>()
                    .ScheduleAsync(message, DateTimeOffset.UtcNow.AddSeconds(2));
                await Assert.That(await probe.Received.Task.WaitAsync(
                        TimeSpan.FromSeconds(20), cancellationToken))
                    .IsEqualTo(message);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    public sealed record ScheduledProbeMessage(Guid Id);

    public sealed class ScheduledProbeHandler(ScheduledProbe probe)
    {
        public Task Handle(ScheduledProbeMessage message)
        {
            probe.Received.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    public sealed class ScheduledProbe
    {
        public TaskCompletionSource<ScheduledProbeMessage> Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
