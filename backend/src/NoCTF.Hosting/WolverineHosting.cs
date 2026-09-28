using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.Configuration;
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

        options.Policies.Add(new AwdpFixVerificationExecutionTimeoutPolicy());
        options.Policies.Add(new AwdFlagInjectionExecutionTimeoutPolicy());

        options.UseNats(nats)
            .AutoProvision()
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
            .DefineWorkQueueStream(
                NatsSubjects.ControlStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Control),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Control)).EnableScheduledDelivery(),
                NatsSubjects.Subject(WorkerQueue.Control))
            .DefineWorkQueueStream(
                NatsSubjects.GameplayStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Gameplay),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Gameplay)).EnableScheduledDelivery(),
                NatsSubjects.Subject(WorkerQueue.Gameplay))
            .DefineWorkQueueStream(
                NatsSubjects.ProjectionStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Projection)),
                NatsSubjects.Subject(WorkerQueue.Projection))
            .DefineWorkQueueStream(
                NatsSubjects.BackgroundStream,
                stream => stream.WithSubjects(
                    NatsSubjects.Subject(WorkerQueue.Background),
                    NatsSubjects.ScheduledSubject(WorkerQueue.Background)).EnableScheduledDelivery(),
                NatsSubjects.Subject(WorkerQueue.Background))
            .DefineWorkQueueStream(
                NatsSubjects.WebhookStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Webhook)),
                NatsSubjects.Subject(WorkerQueue.Webhook))
            .DefineWorkQueueStream(
                NatsSubjects.EventsStream,
                stream => stream.WithSubjects(
                    NatsSubjects.RealtimeEvents,
                    NatsSubjects.LeaderboardEvents,
                    NatsSubjects.WebhookEvents),
                NatsSubjects.RealtimeEvents,
                NatsSubjects.LeaderboardEvents,
                NatsSubjects.WebhookEvents)
            .DefineWorkQueueStream(
                NatsSubjects.RunnerStream,
                stream => stream.WithSubject("noctf.v2.runner.>").EnableScheduledDelivery(),
                "noctf.v2.runner.>");
    }
}
