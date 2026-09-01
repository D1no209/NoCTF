using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class JetStreamLeaseRenewalTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Handler_longer_than_AckWait_is_not_redelivered(
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
            var subject = $"noctf.tests.lease.{suffix}";
            var stream = $"noctf-tests-lease-{suffix}";
            var consumer = $"noctf-tests-consumer-{suffix}";
            var connectionString =
                $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}";
            var probe = new LongRunningProbe();
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddSingleton(probe))
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(LongRunningMessageHandler));
                    options.UseNats(connectionString)
                        .AutoProvision()
                        .UseJetStream(defaults =>
                        {
                            defaults.AckWait = TimeSpan.FromSeconds(1);
                            defaults.MaxDeliver = 3;
                        })
                        .DefineWorkQueueStream(
                            stream,
                            configuration => configuration.WithSubjects(subject),
                            subject);
                    options.ListenToNatsSubject(subject)
                        .UseJetStream(stream, consumer)
                        .AckWait(TimeSpan.FromSeconds(1))
                        .MaximumAckExtension(TimeSpan.FromSeconds(15));
                    options.PublishMessage<LongRunningMessage>()
                        .ToNatsSubject(subject);
                })
                .Build();
            await host.StartAsync(cancellationToken);
            try
            {
                var bus = host.Services.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new LongRunningMessage(Guid.CreateVersion7()));
                await probe.Completed.Task.WaitAsync(
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

                await Assert.That(probe.Attempts).IsEqualTo(1);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    public sealed record LongRunningMessage(Guid Id);

    public sealed class LongRunningMessageHandler(LongRunningProbe probe)
    {
        public async Task Handle(
            LongRunningMessage message,
            CancellationToken cancellationToken)
        {
            _ = message;
            Interlocked.Increment(ref probe.Attempts);
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            probe.Completed.TrySetResult();
        }
    }

    public sealed class LongRunningProbe
    {
        public int Attempts;
        public TaskCompletionSource Completed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
