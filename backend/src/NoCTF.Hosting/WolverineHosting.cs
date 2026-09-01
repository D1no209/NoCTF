using Microsoft.Extensions.Configuration;
using NoCTF.Application.Messaging;
using NoCTF.Hosting.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Nats;
using Wolverine.Postgresql;

namespace NoCTF.Hosting;

public static class WolverineHosting
{
    public static void ConfigureNoCtfPersistence(
        this WolverineOptions options,
        IConfiguration configuration,
        HostRoles roles)
    {
        var postgres = configuration.GetConnectionString("PostgreSql")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:PostgreSql is required.");
        var nats = configuration.GetConnectionString("Nats")
            ?? configuration["Wolverine:Nats:ConnectionString"]
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Nats or Wolverine:Nats:ConnectionString is required.");

        options.PersistMessagesWithPostgresql(postgres, roles.PersistenceSchema);
        options.UseEntityFrameworkCoreTransactions();
        options.Policies.Add(new AwdpFixVerificationExecutionTimeoutPolicy());

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
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Control)),
                NatsSubjects.Subject(WorkerQueue.Control))
            .DefineWorkQueueStream(
                NatsSubjects.GameplayStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Gameplay)),
                NatsSubjects.Subject(WorkerQueue.Gameplay))
            .DefineWorkQueueStream(
                NatsSubjects.ProjectionStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Projection)),
                NatsSubjects.Subject(WorkerQueue.Projection))
            .DefineWorkQueueStream(
                NatsSubjects.BackgroundStream,
                stream => stream.WithSubjects(NatsSubjects.Subject(WorkerQueue.Background)),
                NatsSubjects.Subject(WorkerQueue.Background))
            .DefineWorkQueueStream(
                NatsSubjects.EventsStream,
                stream => stream.WithSubjects(
                    NatsSubjects.RealtimeEvents,
                    NatsSubjects.LeaderboardEvents),
                NatsSubjects.RealtimeEvents,
                NatsSubjects.LeaderboardEvents)
            .DefineWorkQueueStream(
                NatsSubjects.RunnerStream,
                stream => stream.WithSubject("noctf.runner.>"),
                "noctf.runner.>");
    }
}
