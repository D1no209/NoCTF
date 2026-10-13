using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test, Timeout(300_000)]
    public Task Checker_stale_and_completed_timeouts_do_not_scan_or_dispatch_other_samples(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct, includeChecker: true);
            var clock = new MutableTimeProvider(fixture.Now);
            var setupOutbox = new RecordingOutbox();
            await PrepareCheckerDispatchAsync(options, fixture, clock, setupOutbox, ct);
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, setupOutbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
            var timeout = (AwdCheckerCallbackMissing)setupOutbox.Scheduled.Single().Message;
            var ignored = new RecordingOutbox();

            async Task Expire(AwdCheckerCallbackMissing message)
            {
                await using var db = new NoCtfDbContext(options);
                await new AwdCheckerCoordinator(db, ignored, clock, NullCompetitionEventRecorder.Instance).ExpireAsync(message, ct);
            }

            // At the receipt boundary, even the exact message must not close a still-valid receipt window.
            clock.UtcNow = timeout.OccurredAt;
            await Expire(timeout);
            clock.UtcNow = timeout.OccurredAt.AddSeconds(1);
            foreach (var stale in new[]
            {
                timeout with { GameplayFactId = Guid.NewGuid() },
                timeout with { CompetitionId = Guid.NewGuid() },
                timeout with { CompetitionChallengeId = Guid.NewGuid() },
                timeout with { RuntimeInstanceId = Guid.NewGuid() },
                timeout with { AttemptId = Guid.NewGuid() },
                timeout with { OccurredAt = timeout.OccurredAt.AddSeconds(-1) }
            })
                await Expire(stale);

            await using (var verify = new NoCtfDbContext(options))
            {
                var facts = await verify.GameplayFacts.Include(fact => fact.Awd).ToArrayAsync(ct);
                await Assert.That(facts.Length).IsEqualTo(10);
                await Assert.That(facts.Count(fact => fact.State == GameplayFactState.Queued)).IsEqualTo(1);
                await Assert.That(facts.All(fact => fact.Awd!.Settlement == AwdSettlementState.Provisional)).IsTrue();
                var completed = facts.Single(fact => fact.Id == timeout.GameplayFactId);
                completed.State = GameplayFactState.Completed;
                completed.Result = GameplayFactResult.Correct;
                completed.Awd!.Execution = AwdCheckExecution.Completed;
                completed.Awd.Settlement = AwdSettlementState.Final;
                completed.ConcurrencyStamp = Guid.NewGuid();
                await verify.SaveChangesAsync(ct);
            }
            // There is an entirely unplanned next round as well as due siblings in the previous round.
            clock.UtcNow = fixture.Now.AddMinutes(5);
            await PrepareCheckerDispatchAsync(options, fixture, clock, setupOutbox, ct);
            await Expire(timeout);
            await Assert.That(ignored.Published).IsEmpty();
            await Assert.That(ignored.RunnerNodeMessages).IsEmpty();
            await Assert.That(ignored.Scheduled).IsEmpty();
            await using var final = new NoCtfDbContext(options);
            await Assert.That(await final.AwdRounds.CountAsync(ct)).IsEqualTo(2);
            await Assert.That(await final.GameplayFacts.CountAsync(ct)).IsEqualTo(10);
            await Assert.That(await final.Notifications.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await final.GameplayFacts.CountAsync(fact => fact.State == GameplayFactState.Pending, ct)).IsEqualTo(9);
        });

    [Test, Arguments(false), Arguments(true), Timeout(300_000)]
    public Task Checker_timeout_settles_only_its_matching_attempt_and_is_idempotent(bool started, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct, includeChecker: true);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            await PrepareCheckerDispatchAsync(options, fixture, clock, outbox, ct);
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
            var timeout = (AwdCheckerCallbackMissing)outbox.Scheduled.Single().Message;
            if (started)
            {
                await using var db = new NoCtfDbContext(options);
                var fact = await db.GameplayFacts.Include(item => item.Awd).SingleAsync(item => item.Id == timeout.GameplayFactId, ct);
                fact.State = GameplayFactState.Processing;
                fact.Awd!.Execution = AwdCheckExecution.Running;
                fact.Awd.AttemptStartedAt = fixture.Now;
                fact.Awd.AttemptDeadline = fixture.Now.AddSeconds(30);
                fact.ConcurrencyStamp = Guid.NewGuid();
                await db.SaveChangesAsync(ct);
            }
            var expiryOutbox = new RecordingOutbox();
            clock.UtcNow = timeout.OccurredAt.AddMilliseconds(1);
            for (var delivery = 0; delivery < 2; delivery++)
            {
                await using var db = new NoCtfDbContext(options);
                await new AwdCheckerCoordinator(db, expiryOutbox, clock, NullCompetitionEventRecorder.Instance).ExpireAsync(timeout, ct);
            }
            await using var verify = new NoCtfDbContext(options);
            var expired = await verify.GameplayFacts.Include(fact => fact.Awd).SingleAsync(fact => fact.Id == timeout.GameplayFactId, ct);
            await Assert.That(expired.State).IsEqualTo(GameplayFactState.PlatformFailed);
            await Assert.That(expired.Result).IsNull();
            await Assert.That(expired.Awd!.Settlement).IsEqualTo(AwdSettlementState.PendingAdjudication);
            await Assert.That(expired.Awd.Reason).IsEqualTo(started ? AwdCheckReason.CallbackMissing : AwdCheckReason.TaskNotExecuted);
            await Assert.That(await verify.GameplayFacts.CountAsync(fact => fact.State == GameplayFactState.Pending, ct)).IsEqualTo(9);
            await Assert.That(await verify.Notifications.CountAsync(ct)).IsEqualTo(1);
            await Assert.That(expiryOutbox.Published.OfType<GameplayFactStateChanged>().Count()).IsEqualTo(1);
            await Assert.That(expiryOutbox.RunnerNodeMessages).IsEmpty();
            await Assert.That(expiryOutbox.Scheduled).IsEmpty();
        });

    [Test, Timeout(300_000)]
    public Task Checker_timeout_during_pause_preserves_unstarted_attempt_for_resumed_dispatch(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct, includeChecker: true);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            await PrepareCheckerDispatchAsync(options, fixture, clock, outbox, ct);
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
            var timeout = (AwdCheckerCallbackMissing)outbox.Scheduled.Single().Message;

            async Task Transition(CompetitionStatus from, CompetitionStatus to)
            {
                await using var db = new NoCtfDbContext(options);
                var lifecycle = new CompetitionLifecycleStore(db, null!, outbox, new CompetitionEventStore(db, outbox), clock: clock);
                await Assert.That(await lifecycle.TryTransitionWithAuditAsync(fixture.CompetitionId, from, to,
                    fixture.OwnerId, "checker timeout pause", false, CompetitionLifecycleEffects.None, ct)).IsTrue();
            }

            clock.UtcNow = fixture.Now.AddSeconds(10);
            await Transition(CompetitionStatus.Running, CompetitionStatus.Paused);
            clock.UtcNow = fixture.Now.AddSeconds(50);
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance).ExpireAsync(timeout, ct);
            clock.UtcNow = fixture.Now.AddSeconds(70);
            await Transition(CompetitionStatus.Paused, CompetitionStatus.Running);
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance)
                    .DispatchAsync(new(clock.UtcNow), ct);
            // Delivery of the old timeout after resumption cannot expire the extended receipt window.
            await using (var db = new NoCtfDbContext(options))
                await new AwdCheckerCoordinator(db, outbox, clock, NullCompetitionEventRecorder.Instance).ExpireAsync(timeout, ct);
            await using var verify = new NoCtfDbContext(options);
            var fact = await verify.GameplayFacts.Include(item => item.Awd).SingleAsync(item => item.Id == timeout.GameplayFactId, ct);
            await Assert.That(fact.Awd!.Execution).IsEqualTo(AwdCheckExecution.Pending);
            await Assert.That(fact.Awd.Settlement).IsEqualTo(AwdSettlementState.Provisional);
            await Assert.That(fact.Awd.AttemptId).IsEqualTo(timeout.AttemptId);
            // PostgreSQL normalizes the seed's sub-microsecond timestamp before persisting.
            await Assert.That(fact.Awd.ReceiptDeadline!.Value.Ticks / TimeSpan.TicksPerMicrosecond)
                .IsEqualTo(timeout.OccurredAt.AddSeconds(60).Ticks / TimeSpan.TicksPerMicrosecond);
            await Assert.That(await verify.GameplayFacts.CountAsync(ct)).IsEqualTo(10);
            await Assert.That(await verify.Notifications.CountAsync(ct)).IsEqualTo(0);
        });

    [Test, Timeout(300_000)]
    public Task Checker_dispatch_reads_are_batched_while_all_team_samples_and_manifests_are_preserved(CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            var readCounts = new List<int>();
            foreach (var teamCount in new[] { 1, 12 })
            {
                await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
                await postgres.StartAsync(ct);
                var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                    .UseSnakeCaseNamingConvention().Options;
                var fixture = await SeedAsync(options, ct, includeChecker: true);
                await using (var setup = new NoCtfDbContext(options))
                {
                    for (var index = 1; index < teamCount; index++)
                    {
                        var userId = Guid.NewGuid();
                        var teamId = Guid.NewGuid();
                        var runtimeId = Guid.NewGuid();
                        setup.Users.Add(new User { Id = userId, UserName = $"checker-{index}", NormalizedUserName = $"CHECKER-{index}",
                            Email = $"checker-{index}@example.test", PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
                        setup.Teams.Add(new Team { Id = teamId, CompetitionId = fixture.CompetitionId, CaptainId = userId,
                            MemberIds = [userId], Name = $"Checker {index}", InvitationToken = Guid.NewGuid().ToString("N"),
                            RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now });
                        setup.RuntimeInstances.Add(new PlayerRuntimeInstance { Id = runtimeId, CompetitionId = fixture.CompetitionId,
                            CompetitionChallengeId = fixture.CompetitionChallengeId, TeamId = teamId, RuntimeKind = RuntimeKind.Container,
                            RuntimeProvider = RuntimeProvider.Docker, RunnerId = "runner-a", State = RuntimeState.Running,
                            ProviderReceipt = RuntimeReceiptTestData.ContainerEntity(runtimeId), CreatedAt = fixture.Now, RunningAt = fixture.Now });
                    }
                    await setup.SaveChangesAsync(ct);
                }
                var clock = new MutableTimeProvider(fixture.Now);
                var outbox = new RecordingOutbox();
                await PrepareCheckerDispatchAsync(options, fixture, clock, outbox, ct);
                var reads = new CheckerDispatchReadCounter();
                var measuredOptions = new DbContextOptionsBuilder<NoCtfDbContext>(options).AddInterceptors(reads).Options;
                await using (var dispatchDb = new NoCtfDbContext(measuredOptions))
                    await new AwdCheckerCoordinator(dispatchDb, outbox, clock, NullCompetitionEventRecorder.Instance)
                        .DispatchAsync(new(clock.UtcNow), ct);
                readCounts.Add(reads.Count);
                await using var verify = new NoCtfDbContext(options);
                var facts = await verify.GameplayFacts.Include(fact => fact.Awd).ThenInclude(details => details!.Flags).ToArrayAsync(ct);
                await Assert.That(facts.Length).IsEqualTo(10 * teamCount);
                await Assert.That(facts.Count(fact => fact.Awd!.FinalScheduledCheck)).IsEqualTo(teamCount);
                await Assert.That(facts.Count(fact => fact.State == GameplayFactState.Queued)).IsEqualTo(teamCount);
                await Assert.That(facts.Where(fact => fact.State == GameplayFactState.Queued)
                    .All(fact => fact.Awd!.ManifestComplete && fact.Awd.Flags.Count == 1)).IsTrue();
                await Assert.That(facts.Where(fact => fact.Awd!.FinalScheduledCheck)
                    .All(fact => fact.Awd!.SampleDeadlineActiveTicks - fact.Awd.SampleActiveTicks == TimeSpan.FromSeconds(30).Ticks)).IsTrue();
                await Assert.That(outbox.RunnerNodeMessages.OfType<RunAwdChecker>().Count()).IsEqualTo(teamCount);
            }
            // Increasing independent targets and future planned samples must not add per-fact reads.
            await Assert.That(readCounts[1]).IsLessThanOrEqualTo(readCounts[0] + 2);
        });

    private static async Task PrepareCheckerDispatchAsync(DbContextOptions<NoCtfDbContext> options, Fixture fixture,
        MutableTimeProvider clock, RecordingOutbox outbox, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        var definition = (AwdChallengeDefinition)(await db.Challenges.SingleAsync(ct)).Definition!;
        definition.Checker!.TargetServiceName = "main";
        definition.FlagStores.Single().DeliveryMode = AwdFlagDeliveryMode.BusinessChecker;
        await db.SaveChangesAsync(ct);
        var rounds = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
        await rounds.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
        await rounds.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
    }

    private sealed class CheckerDispatchReadCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Count++;
            return ValueTask.FromResult(result);
        }
    }
}
