using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Common;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class AtomicAggregatePatchPersistenceTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Cache_invalidation_runs_after_commit_and_before_outbox_dispatch(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_atomic_post_commit")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;
            await using (var setup = new NoCtfDbContext(options))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            var order = new List<string>();
            await using var db = new NoCtfDbContext(options);
            var postCommit = new AggregatePatchPostCommitActions();
            IAtomicAggregatePatch patch = new AggregatePatchTransaction(
                db,
                new RecordingOutbox(order),
                postCommit);

            var outcome = await patch.ExecuteAsync(async ct =>
            {
                var settings = await db.PlatformSettings.SingleAsync(ct);
                settings.Name = "committed before invalidation";
                await db.SaveChangesAsync(ct);
                await postCommit.RunOrDeferAsync(async token =>
                {
                    await using var verification = new NoCtfDbContext(options);
                    var persisted = await verification.PlatformSettings.AsNoTracking()
                        .Select(item => item.Name)
                        .SingleAsync(token);
                    if (persisted != "committed before invalidation")
                        throw new InvalidOperationException("Cache invalidation ran before commit.");
                    order.Add("cache");
                }, ct);
                await Assert.That(order).IsEmpty();
                return AtomicAggregatePatchDecision<string>.Commit("committed");
            }, cancellationToken);

            await Assert.That(outcome).IsEqualTo("committed");
            await Assert.That(order).Count().IsEqualTo(2);
            await Assert.That(order[0]).IsEqualTo("cache");
            await Assert.That(order[1]).IsEqualTo("outbox");
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Later_section_rejection_rolls_back_earlier_saved_sections(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder(
                    "postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_atomic_aggregate_patch")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();
            await postgres.StartAsync(cancellationToken);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options;

            await using (var setup = new NoCtfDbContext(options))
                await setup.Database.EnsureCreatedAsync(cancellationToken);

            await using (var db = new NoCtfDbContext(options))
            {
                IAtomicAggregatePatch patch = new AggregatePatchTransaction(
                    db,
                    new NoOpPostCommitMessagePublisher());
                var outcome = await patch.ExecuteAsync(async ct =>
                {
                    await using var sectionTransaction =
                        await AggregateCompatibleTransaction.BeginAsync(db, ct);
                    var settings = await db.PlatformSettings.SingleAsync(ct);
                    settings.Name = "must roll back";
                    await db.SaveChangesAsync(ct);
                    await sectionTransaction.CommitAsync(ct);
                    return AtomicAggregatePatchDecision<string>.Rollback("rejected");
                }, cancellationToken);

                await Assert.That(outcome).IsEqualTo("rejected");
                await Assert.That(db.ChangeTracker.Entries()).IsEmpty();
            }

            await using (var verification = new NoCtfDbContext(options))
            {
                var settings = await verification.PlatformSettings.AsNoTracking()
                    .SingleAsync(cancellationToken);
                await Assert.That(settings.Name).IsEqualTo("NoCTF");
            }
        });
    }

    private sealed class RecordingOutbox(List<string> order) : IPostCommitMessagePublisher
    {
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : IRunnerNodeMessage => ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync()
        {
            order.Add("outbox");
            return Task.CompletedTask;
        }
    }
}
