using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
public sealed class WolverineTransactionalOutboxTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Consumer_observes_business_fact_committed_with_outbox(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("noctf_wolverine_test")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);

            using var host = BuildHost(postgres.GetConnectionString());
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                await db.Database.ExecuteSqlRawAsync(
                    "CREATE TABLE outbox_business_probe (id uuid PRIMARY KEY)",
                    cancellationToken);
            }

            await host.StartAsync(cancellationToken);
            try
            {
                var id = Guid.CreateVersion7();
                var observation = OutboxProbeObservation.Expect(id);
                var bus = host.Services.GetRequiredService<IMessageBus>();

                await bus.SendAsync(new WriteOutboxBusinessProbe(id));

                await Assert.That(await observation.WaitAsync(cancellationToken)).IsTrue();
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    private static IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(
            options => options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        builder.UseWolverine(options =>
        {
            options.Discovery.IncludeType<OutboxBusinessProbeHandler>();
            options.Discovery.IncludeType<ObserveOutboxBusinessProbeHandler>();
            options.PersistMessagesWithPostgresql(connectionString, "wolverine_test");
            options.UseEntityFrameworkCoreTransactions();
            options.AutoBuildMessageStorageOnStartup = JasperFx.AutoCreate.All;
            options.ListenToPostgresqlQueue("outbox-probe").UseDurableInbox();
            options.PublishMessage<WriteOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
            options.PublishMessage<ObserveOutboxBusinessProbe>()
                .ToPostgresqlQueue("outbox-probe");
        });
        return builder.Build();
    }
}

public sealed record WriteOutboxBusinessProbe(Guid Id);
public sealed record ObserveOutboxBusinessProbe(Guid Id);

public sealed class OutboxBusinessProbeHandler
{
    public static async Task Handle(
        WriteOutboxBusinessProbe message,
        NoCtfDbContext db,
        IDbContextOutbox<NoCtfDbContext> outbox,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO outbox_business_probe (id) VALUES ({message.Id})",
            cancellationToken);
        await outbox.PublishAsync(new ObserveOutboxBusinessProbe(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }
}

public sealed class ObserveOutboxBusinessProbeHandler
{
    public static async Task Handle(
        ObserveOutboxBusinessProbe message,
        NoCtfDbContext db,
        CancellationToken cancellationToken)
    {
        var count = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS value FROM outbox_business_probe WHERE id = {message.Id}")
            .SingleAsync(cancellationToken);
        OutboxProbeObservation.Complete(message.Id, count == 1);
    }
}

internal static class OutboxProbeObservation
{
    private static readonly object Sync = new();
    private static readonly Dictionary<Guid, TaskCompletionSource<bool>> Pending = [];

    public static Task<bool> Expect(Guid id)
    {
        lock (Sync)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Add(id, source);
            return source.Task;
        }
    }

    public static void Complete(Guid id, bool factWasVisible)
    {
        TaskCompletionSource<bool>? source;
        lock (Sync)
        {
            if (!Pending.Remove(id, out source)) return;
        }
        source.SetResult(factWasVisible);
    }
}
