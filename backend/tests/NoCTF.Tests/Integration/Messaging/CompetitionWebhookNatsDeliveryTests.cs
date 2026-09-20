using DotNet.Testcontainers.Builders;
using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Competitions.Webhooks;
using NoCTF.Hosting;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[Category("CompetitionWebhooks")]
[NotInParallel]
public sealed class CompetitionWebhookNatsDeliveryTests
{
    [Test]
    [Timeout(180_000)]
    public async Task Delivery_commands_use_the_durable_webhook_stream(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder(
                    "docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true)
                .WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222))
                .Build();
            await Task.WhenAll(
                postgres.StartAsync(cancellationToken),
                nats.StartAsync(cancellationToken));
            var probe = new WebhookDeliveryProbe();
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddSingleton(probe))
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(WebhookDeliveryProbeHandler));
                    options.PersistMessagesWithPostgresql(
                        postgres.GetConnectionString(),
                        "webhook_delivery_test");
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    options.UseNats(
                            $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}")
                        .AutoProvision()
                        .UseJetStream(_ => { })
                        .DefineWorkQueueStream(
                            NatsSubjects.WebhookStream,
                            stream => stream.WithSubject(NatsSubjects.Subject(
                                NoCTF.Application.Messaging.WorkerQueue.Webhook)),
                            NatsSubjects.Subject(
                                NoCTF.Application.Messaging.WorkerQueue.Webhook));
                    options.PublishMessage<DeliverCompetitionWebhook>()
                        .ToNatsSubject(NatsSubjects.Subject(
                            NoCTF.Application.Messaging.WorkerQueue.Webhook))
                        .UseJetStream(NatsSubjects.WebhookStream)
                        .UseDurableOutbox();
                    options.ListenToNatsSubject(NatsSubjects.Subject(
                            NoCTF.Application.Messaging.WorkerQueue.Webhook))
                        .UseJetStream(NatsSubjects.WebhookStream, "webhook-delivery-test")
                        .UseDurableInbox();
                })
                .Build();
            await host.StartAsync(cancellationToken);
            try
            {
                var expected = new DeliverCompetitionWebhook(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    DateTimeOffset.UtcNow);
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(expected);
                var actual = await probe.Received.Task.WaitAsync(
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
                await Assert.That(actual).IsEqualTo(expected);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    public sealed class WebhookDeliveryProbe
    {
        public TaskCompletionSource<DeliverCompetitionWebhook> Received { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class WebhookDeliveryProbeHandler(WebhookDeliveryProbe probe)
    {
        public void Handle(DeliverCompetitionWebhook message) =>
            probe.Received.TrySetResult(message);
    }
}
