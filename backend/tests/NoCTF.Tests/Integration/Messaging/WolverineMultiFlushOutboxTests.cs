using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace NoCTF.Tests.Integration.Messaging;

[Category("Integration")]
[NotInParallel]
public sealed class WolverineMultiFlushOutboxTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Message_added_after_an_earlier_save_is_persisted_and_delivered_once(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase($"noctf_wolverine_multi_flush_{Guid.NewGuid():N}")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var connectionString = postgres.GetConnectionString();
            var dbOptions = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var setup = new NoCtfDbContext(dbOptions))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            var probe = new MultiFlushProbeState();
            using var host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(probe);
                    services.AddDbContextWithWolverineIntegration<NoCtfDbContext>(options =>
                        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
                    services.AddScoped<ITransactionalMessageOutbox,
                        WolverineTransactionalMessageOutbox>();
                })
                .UseWolverine(options =>
                {
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType(typeof(MultiFlushProbeMessageHandler));
                    options.PersistMessagesWithPostgresql(
                        connectionString,
                        "wolverine_multi_flush");
                    options.UseEntityFrameworkCoreTransactions();
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.All;
                    options.LocalQueue("multi-flush-probe").UseDurableInbox();
                    options.PublishMessage<MultiFlushProbeMessage>()
                        .ToLocalQueue("multi-flush-probe");
                })
                .Build();
            await host.StartAsync(cancellationToken);
            try
            {
                using var scope = host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NoCtfDbContext>();
                var outbox = scope.ServiceProvider
                    .GetRequiredService<ITransactionalMessageOutbox>();
                var now = DateTimeOffset.UtcNow;
                var user = new User
                {
                    Id = Guid.CreateVersion7(now),
                    UserName = "multi-flush",
                    NormalizedUserName = "MULTI-FLUSH",
                    Email = "multi-flush@example.test",
                    PasswordHash = "unused",
                    Kind = UserKind.Human,
                    Role = UserRole.User,
                    AccountStatus = UserAccountStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(cancellationToken);

                var messageId = Guid.CreateVersion7(now.AddTicks(1));
                await outbox.PublishAsync(new MultiFlushProbeMessage(messageId));
                user.UpdatedAt = now.AddTicks(1);
                await db.SaveChangesAsync(cancellationToken);
                await outbox.FlushOutgoingMessagesAsync();

                await probe.Delivered.Task.WaitAsync(
                    TimeSpan.FromSeconds(15),
                    cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                await Assert.That(probe.Attempts).IsEqualTo(1);
                await Assert.That(probe.MessageId).IsEqualTo(messageId);
            }
            finally
            {
                await host.StopAsync(cancellationToken);
            }
        });
    }

    public sealed record MultiFlushProbeMessage(Guid Id);

    public sealed class MultiFlushProbeMessageHandler(MultiFlushProbeState probe)
    {
        public void Handle(MultiFlushProbeMessage message)
        {
            probe.MessageId = message.Id;
            Interlocked.Increment(ref probe.Attempts);
            probe.Delivered.TrySetResult();
        }
    }

    public sealed class MultiFlushProbeState
    {
        public int Attempts;
        public Guid MessageId { get; set; }
        public TaskCompletionSource Delivered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
