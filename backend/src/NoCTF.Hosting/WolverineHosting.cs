using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.Configuration;
using NATS.Client.JetStream.Models;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.Messaging;
using NoCTF.Hosting.Messaging;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Hosting;

public static class WolverineHosting
{
    public static void ConfigureNoCtfPersistence(
        this WolverineOptions options,
        IConfiguration configuration,
        HostRoles roles)
    {
        options.ServiceLocationPolicy = ServiceLocationPolicy.NotAllowed;

        var nats = configuration.GetConnectionString("Nats")
            ?? configuration["Wolverine:Nats:ConnectionString"]
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Nats or Wolverine:Nats:ConnectionString is required.");

        options.Policies.Add(new CapabilityExecutionTimeoutPolicy());
        options.Policies.Add(new AwdFlagInjectionExecutionTimeoutPolicy());

        options.UseNats(nats)
            .AutoProvision()
            .DeduplicateUsing(envelope => $"{envelope.DeduplicationId ?? envelope.Id.ToString()}:{envelope.Destination?.AbsoluteUri
                ?? throw new InvalidOperationException("JetStream publication requires a destination.")}")
            .UseJetStream(jetStream =>
            {
                jetStream.MaxDeliver = configuration.GetValue("Wolverine:Nats:MaxDeliver", 5);
                jetStream.AckWait = TimeSpan.FromSeconds(configuration.GetValue(
                    "Wolverine:Nats:AckWaitSeconds",
                    30));
                jetStream.DuplicateWindow = TimeSpan.FromSeconds(configuration.GetValue(
                    "Wolverine:Nats:DuplicateWindowSeconds",
                    120));
            })
            .DefineStream(
                NatsSubjects.ControlStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Control),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Control) + ".>").EnableScheduledDelivery())
            .DefineStream(
                NatsSubjects.GameplayStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Gameplay),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Gameplay) + ".>").EnableScheduledDelivery())
            .DefineStream(
                NatsSubjects.LiveSoloMediaStream,
                stream=>stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.LiveSoloMedia),
                    NatsSubjects.ScheduledSubject(WorkerQueue.LiveSoloMedia) + ".>").EnableScheduledDelivery())
            .DefineStream(
                NatsSubjects.ProjectionStream,
                stream =>
                {
                    stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Projection));
                    stream.Retention = StreamConfigRetention.Workqueue;
                })
            .DefineStream(
                NatsSubjects.BackgroundStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Background),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Background) + ".>").EnableScheduledDelivery())
            .DefineWorkQueueStream(
                NatsSubjects.WebhookStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Webhook)),
                NatsSubjects.Subject(WorkerQueue.Webhook))
            .DefineWorkQueueStream(
                NatsSubjects.NotificationsStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Notifications)),
                NatsSubjects.Subject(WorkerQueue.Notifications))
            .DefineWorkQueueStream(
                NatsSubjects.EventsStream,
                stream => stream.WithSubjects(
                    NatsSubjects.RealtimeEvents,
                    NatsSubjects.LeaderboardEvents,
                    NatsSubjects.WebhookEvents),
                NatsSubjects.RealtimeEvents,
                NatsSubjects.LeaderboardEvents,
                NatsSubjects.WebhookEvents)
            .DefineStream(
                NatsSubjects.RunnerStream,
                stream => stream.WithSubject("noctf.v2.runner.>").EnableScheduledDelivery());
    }
}
