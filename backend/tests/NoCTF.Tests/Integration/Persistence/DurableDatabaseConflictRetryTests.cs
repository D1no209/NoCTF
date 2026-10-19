using System.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.JetStream.Models;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Hosting;
using NoCTF.Hosting.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Nats;

namespace NoCTF.Tests.Integration.Persistence;

public sealed class DurableDatabaseConflictRetryTests
{
    [Test, Timeout(120_000), NotInParallel]
    public Task Wrapped_serialization_failure_is_retried_through_real_JetStream(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await using var nats = new ContainerBuilder("nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d")
                .WithPortBinding(4222, true).WithCommand("-js")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(4222)).Build();
            await Task.WhenAll(postgres.StartAsync(ct), nats.StartAsync(ct));
            var database = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var identity = Guid.NewGuid();
            await using (var seed = new NoCtfDbContext(database))
            {
                await seed.Database.EnsureCreatedAsync(ct);
                seed.Users.Add(new User { Id = identity, UserName = "retry", NormalizedUserName = "RETRY",
                    Email = "retry@example.test", PasswordHash = "unused", CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow });
                await seed.SaveChangesAsync(ct);
            }
            var state = new DatabaseConflictRetryState(database);
            using var host = Host.CreateDefaultBuilder().ConfigureServices(services => services.AddSingleton(state))
                .UseWolverine(options =>
                {
                    options.UseRuntimeCompilation();
                    options.Discovery.DisableConventionalDiscovery();
                    options.Discovery.IncludeType<DatabaseConflictRetryHandler>();
                    options.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}")
                        .AutoProvision().UseJetStream(_ => { }).DefineStream("DATABASE_RETRY", stream =>
                        {
                            stream.WithSubject(NatsSubjects.Subject(WorkerQueue.Gameplay));
                            stream.Retention = StreamConfigRetention.Workqueue;
                        });
                    options.ListenToNatsSubject(NatsSubjects.Subject(WorkerQueue.Gameplay))
                        .UseJetStream("DATABASE_RETRY", WorkerQueueNames.Gameplay).Named(WorkerQueueNames.Gameplay);
                    options.PublishMessage<DatabaseConflictRetry>().ToNatsSubject(NatsSubjects.Subject(WorkerQueue.Gameplay))
                        .UseJetStream("DATABASE_RETRY");
                    options.ConfigureNoCtfInfrastructureRetriesFor<DatabaseConflictRetry>(WorkerQueue.Gameplay, WorkerQueueNames.Gameplay);
                }).Build();
            await host.StartAsync(ct);
            try
            {
                await host.Services.GetRequiredService<IMessageBus>().PublishAsync(new DatabaseConflictRetry(identity));
                await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                await Assert.That(state.SerializationFailures).IsEqualTo(1);
                await Assert.That(state.FailureSqlState).IsEqualTo("40001");
                await Assert.That(state.WrappedFailure).IsTrue();
                await Assert.That(state.Attempts).IsEqualTo(2);
                await using var verify = new NoCtfDbContext(database);
                await Assert.That((await verify.Users.SingleAsync(user => user.Id == identity, ct)).TokenVersion).IsEqualTo(2);
            }
            finally { await host.StopAsync(ct); }
        });
}

public sealed record DatabaseConflictRetry(Guid UserId);

public sealed class DatabaseConflictRetryState(DbContextOptions<NoCtfDbContext> options)
{
    public DbContextOptions<NoCtfDbContext> Options { get; } = options;
    public int Attempts;
    public int SerializationFailures;
    public string? FailureSqlState;
    public bool WrappedFailure;
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class DatabaseConflictRetryHandler
{
    public static async Task Handle(DatabaseConflictRetry message, DatabaseConflictRetryState state, CancellationToken ct)
    {
        var attempt = Interlocked.Increment(ref state.Attempts);
        await using var db = new NoCtfDbContext(state.Options);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var user = await db.Users.SingleAsync(item => item.Id == message.UserId, ct);
        if (attempt == 1)
        {
            await using var concurrent = new NoCtfDbContext(state.Options);
            var other = await concurrent.Users.SingleAsync(item => item.Id == message.UserId, ct);
            other.TokenVersion++;
            await concurrent.SaveChangesAsync(ct);
        }
        user.TokenVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (TransactionFailureClassifier.IsRetryable(exception))
        {
            state.WrappedFailure = exception is InvalidOperationException;
            for (Exception? current = exception; current is not null; current = current.InnerException)
                if (current is System.Data.Common.DbException database) state.FailureSqlState = database.SqlState;
            Interlocked.Increment(ref state.SerializationFailures);
            throw;
        }
        state.Completed.SetResult();
    }
}
