using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Tests.Fixtures.PostgreSql;
using NoCTF.Worker;

namespace NoCTF.Tests.Integration.Runtime;

[Category("Integration"), NotInParallel]
public sealed class RuntimeDispatchTransactionTests
{
    [Test, Timeout(300_000)]
    public async Task A_post_commit_publisher_failure_does_not_replay_the_dispatch_transaction(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture();
            await postgres.InitializeAsync();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var messages = new DispatchPublisher { FailAfterCommit = true };

            // A missing Runtime is a valid no-op dispatch, but still traverses the actual
            // PostgreSQL transaction/commit/publication boundary without provider effects.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            {
                await DispatchAsync(db, messages, ct);
            });
            await Assert.That(messages.CommitFlushes).IsEqualTo(1);
            await Assert.That(messages.DiscardedBatches).IsEqualTo(0);
        });
    }

    [Test, Timeout(300_000)]
    public async Task An_ambient_transaction_failure_is_returned_to_its_owner_without_clearing_pending_changes(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlContainerFixture();
            await postgres.InitializeAsync();
            var fault = new RejectRuntimeRead();
            var options = new DbContextOptionsBuilder<NoCtfDbContext>()
                .UseNpgsql(postgres.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(fault).Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            await RuntimeCapacityDomainFixture.SeedAsync(db, 0, ct);
            var user = await db.Users.SingleAsync(ct);
            user.UserName = "pending-caller-edit";
            var messages = new DispatchPublisher();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () =>
            {
                await DispatchAsync(db, messages, ct);
            });
            db.ChangeTracker.DetectChanges();
            await Assert.That(fault.Attempts).IsEqualTo(1);
            await Assert.That(db.Entry(user).State).IsEqualTo(EntityState.Modified);
            await Assert.That(user.UserName).IsEqualTo("pending-caller-edit");
            await Assert.That(messages.DiscardedBatches).IsEqualTo(0);
            await Assert.That(messages.CommitFlushes).IsEqualTo(0);
            await transaction.RollbackAsync(ct);
        });
    }

    private static Task DispatchAsync(NoCtfDbContext db, IPostCommitMessagePublisher messages, CancellationToken ct) =>
        BackendMessageOperations.DispatchRuntimeAsync(new DispatchRuntime(Guid.NewGuid()), db,
            Substitute.For<IChallengeRuntimeTemplateCatalog>(), Substitute.For<IRuntimePlacementPolicy>(),
            Substitute.For<IRunnerCapacityGate>(), messages, TimeProvider.System, ct);

    private sealed class RejectRuntimeRead : DbCommandInterceptor
    {
        public int Attempts { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("runtime_instances", StringComparison.Ordinal))
            {
                Attempts++;
                throw new DbUpdateConcurrencyException("Injected isolated dispatch read conflict.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DispatchPublisher : IPostCommitMessagePublisher
    {
        public bool FailAfterCommit { get; init; }
        public int CommitFlushes { get; private set; }
        public int DiscardedBatches { get; private set; }
        public ValueTask PublishAsync<T>(T message) => ValueTask.CompletedTask;
        public ValueTask PublishToRunnerNodeAsync<T>(T message) where T : IRunnerNodeMessage => ValueTask.CompletedTask;
        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset at) => throw new NotSupportedException();
        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset at) where T : IRunnerNodeMessage => throw new NotSupportedException();
        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;
        public void DiscardPendingMessages() => DiscardedBatches++;
        public Task FlushCommittedMessagesAsync()
        {
            CommitFlushes++;
            return FailAfterCommit
                ? Task.FromException(new DbUpdateConcurrencyException("Injected isolated post-commit publication failure."))
                : Task.CompletedTask;
        }
    }
}
