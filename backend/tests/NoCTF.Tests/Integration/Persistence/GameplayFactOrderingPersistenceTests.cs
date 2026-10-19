using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Application.Runtime.Capacity;
using NoCTF.Worker;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Runtime;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Scoring.Leaderboard;
using NoCTF.GameModes.Leaderboard;
using NoCTF.Application.Notifications;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class GameplayFactOrderingPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Challenge_detail_projects_runtime_capabilities_without_loading_definitions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_challenge_capabilities", ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, ct);
            var commands = new CapabilityReadCommands();
            await using var db = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(options)
                .AddInterceptors(commands).Options);
            var store = new NoCTF.Infrastructure.Challenges.Management.ChallengeManagementStore(
                db, new RecordingOutbox(), new ChallengeRuntimeTemplateCatalog());
            var initial = await store.FindAsync(fixture.CompetitionId, fixture.CompetitionChallengeId, true, false, ct);
            await Assert.That(initial!.HasRuntime).IsFalse();
            await Assert.That(initial.UsesDynamicFlag).IsFalse();
            var template = await db.Challenges.AsSplitQuery().SingleAsync(ct);
            template.Definition!.Runtime = new ContainerChallengeRuntimeTemplate
            {
                ChallengeId = template.Id,
                FlagSource = PersistedRuntimeFlagSource.PerTeam,
                Services = [new() { ChallengeId = template.Id, Name = "target", Image = "alpine:3" }]
            };
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            commands.Texts.Clear();
            var current = await store.FindAsync(fixture.CompetitionId, fixture.CompetitionChallengeId, true, false, ct);
            await Assert.That(current!.HasRuntime).IsTrue();
            await Assert.That(current.UsesDynamicFlag).IsTrue();
            await Assert.That(current.InteractionKind).IsEqualTo(CtfInteractionKind.FlagSubmission);
            await Assert.That(db.ChangeTracker.Entries().Any()).IsFalse();
            await Assert.That(commands.Texts.Count).IsGreaterThan(0);
            Console.WriteLine($"ChallengeCapabilityRead SQL commands: {commands.Texts.Count}");
            await Assert.That(commands.Texts.Any(sql => sql.Contains("runtime_services", StringComparison.OrdinalIgnoreCase)
                || sql.Contains("runtime_service_commands", StringComparison.OrdinalIgnoreCase))).IsFalse();
        });
    }

    private sealed class CapabilityReadCommands : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    [Test, Timeout(300_000)]
    [Arguments(GameplayFactKind.FlagAttempt)]
    [Arguments(GameplayFactKind.ManualAdjustment)]
    public async Task Duplicate_delivery_and_old_revision_do_not_complete_twice(GameplayFactKind kind, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_duplicate_fact", ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, ct);
            var fact = Fact(fixture, Guid.CreateVersion7(), fixture.TeamIds[0], fixture.Now, kind: kind);
            if (kind == GameplayFactKind.ManualAdjustment)
            {
                fact.Value = "25";
                fact.ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(fact.Value));
            }
            await AddFactsAsync(options, [fact], ct);
            Guid stamp;
            await using (var read = new NoCtfDbContext(options))
                stamp = await read.GameplayFacts.Where(item => item.Id == fact.Id)
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct);
            var writes = new ConcurrentBag<string>();
            async Task Deliver(Guid expected)
            {
                var commands = new CapabilityReadCommands();
                await using var workerDb = new NoCtfDbContext(new DbContextOptionsBuilder<NoCtfDbContext>(options)
                    .AddInterceptors(commands).Options);
                var outgoing = new RecordingOutbox();
                var processor = new GameplayFactProcessor(workerDb, new GameModeGameplayFactEvaluatorCatalog(),
                    outgoing, eventRecorder: new CompetitionEventStore(workerDb, outgoing));
                await processor.ProcessAsync(fact.Id, expected, ct);
                foreach (var sql in commands.Texts.Where(sql => sql.Contains("UPDATE gameplay_facts", StringComparison.OrdinalIgnoreCase)
                    || sql.Contains("UPDATE \"gameplay_facts\"", StringComparison.OrdinalIgnoreCase)))
                    writes.Add(sql);
            }
            await Task.WhenAll(Deliver(stamp), Deliver(stamp));
            await Deliver(stamp);
            await Assert.That(writes.Count).IsGreaterThan(0);
            await Assert.That(writes.All(sql => sql.LastIndexOf("WHERE", StringComparison.OrdinalIgnoreCase) is var where && where >= 0
                && sql[where..].Contains("concurrency_stamp", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await using var verify = new NoCtfDbContext(options);
            var saved = await verify.GameplayFacts.SingleAsync(item => item.Id == fact.Id, ct);
            await Assert.That(saved.State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(saved.Result).IsEqualTo(kind == GameplayFactKind.ManualAdjustment
                ? GameplayFactResult.Applied : GameplayFactResult.Correct);
            await Assert.That(saved.ConcurrencyStamp).IsNotEqualTo(stamp);
            await Assert.That(await verify.CompetitionEvents.CountAsync(item => item.GameplayFactId == fact.Id
                && item.Kind == CompetitionEventKind.GameplayFactAdjudicated, ct)).IsEqualTo(1);
            saved.State = GameplayFactState.Queued;
            saved.Result = null;
            await verify.SaveChangesAsync(ct);
            await Deliver(stamp);
            await Assert.That(await verify.GameplayFacts.AsNoTracking().Where(item => item.Id == fact.Id)
                .Select(item => item.State).SingleAsync(ct)).IsEqualTo(GameplayFactState.Queued);
        });
    }

    [Test, Timeout(300_000)]
    public async Task Intake_messages_use_saved_stamps_and_worker_applies_attempt_positions(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_saved_dispatch", ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, ct);
            await using (var configure = new NoCtfDbContext(options))
            {
                var challenge = await configure.CompetitionChallenges.AsSplitQuery().SingleAsync(ct);
                challenge.Rules!.MaxFlagAttempts = 3;
                await configure.SaveChangesAsync(ct);
            }
            var deliveries = new List<EvaluateGameplayFact>();
            for (var index = 0; index < 5; index++)
            {
                await using var intakeDb = new NoCtfDbContext(options);
                var outgoing = new RecordingOutbox();
                var intake = new NoCTF.Infrastructure.GameplayFacts.Intake.GameplayFactIntakeStore(
                    intakeDb, outgoing, new CompetitionEventStore(intakeDb, outgoing));
                var result = await new NoCTF.Application.GameplayFacts.Intake.SubmitFlag(
                    intake, new GameModeGameplayFactAdmissionPolicy()).ExecuteBatchAsync(
                        fixture.CompetitionId, fixture.CompetitionChallengeId, fixture.MemberIds[0],
                        [fixture.Flag], fixture.Now.AddMilliseconds(index), ct);
                await Assert.That(result.Succeeded).IsTrue();
                var message = outgoing.Messages.OfType<EvaluateGameplayFact>().Single();
                var saved = await intakeDb.GameplayFacts.AsNoTracking().SingleAsync(item => item.Id == message.GameplayFactId, ct);
                await Assert.That(message.ExpectedConcurrencyStamp).IsEqualTo(saved.ConcurrencyStamp);
                deliveries.Add(message);
            }
            foreach (var message in deliveries.AsEnumerable().Reverse())
            {
                await using var workerDb = new NoCtfDbContext(options);
                var outgoing = new RecordingOutbox();
                var processor = new GameplayFactProcessor(workerDb, new GameModeGameplayFactEvaluatorCatalog(),
                    outgoing, eventRecorder: new CompetitionEventStore(workerDb, outgoing));
                await processor.ProcessAsync(message.GameplayFactId, message.ExpectedConcurrencyStamp, ct);
                var saved = await workerDb.GameplayFacts.AsNoTracking().SingleAsync(item => item.Id == message.GameplayFactId, ct);
                await Assert.That(saved.State).IsEqualTo(GameplayFactState.Completed);
                await Assert.That(outgoing.Messages.OfType<GameplayFactStateChanged>().Single().ConcurrencyStamp)
                    .IsEqualTo(saved.ConcurrencyStamp);
                await Assert.That(outgoing.Messages.OfType<StopSolvedChallengeRuntimes>().Count())
                    .IsEqualTo(saved.Result == GameplayFactResult.Correct ? 1 : 0);
            }
            await using var verify = new NoCtfDbContext(options);
            var results = await verify.GameplayFacts.AsNoTracking().OrderBy(item => item.OccurredAt)
                .Select(item => item.Result).ToArrayAsync(ct);
            await Assert.That(results.Take(3).All(result => result == GameplayFactResult.Correct)).IsTrue();
            await Assert.That(results.Skip(3).All(result => result == GameplayFactResult.AttemptsExhausted)).IsTrue();
        });
    }

    [Test]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player, RuntimeKind.Container, false)]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player, RuntimeKind.Container, false)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack, RuntimeKind.Container, false)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack, RuntimeKind.Container, false)]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player, RuntimeKind.Container, true)]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player, RuntimeKind.Container, true)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack, RuntimeKind.Container, true)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack, RuntimeKind.Container, true)]
    [Timeout(300_000)]
    public async Task Old_correct_submission_never_stops_a_reopened_runtime_but_TTL_still_does(
        GameMode mode, GameplayFactKind kind, RuntimePurpose purpose, RuntimeKind runtimeKind,
        bool delayedResult, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_reopened_runtime", ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 2, ct, mode);
            var factId = Guid.NewGuid();
            var originalId = Guid.NewGuid();
            var reopenedId = Guid.NewGuid();
            var unrelatedId = Guid.NewGuid();
            await using (var setup = new NoCtfDbContext(options))
            {
                var original = Runtime(fixture, originalId, fixture.TeamIds[0], purpose);
                original.RuntimeKind = runtimeKind;
                if (delayedResult)
                {
                    original.State = RuntimeState.Stopped;
                    original.StoppedAt = fixture.Now.AddMilliseconds(500);
                }
                setup.RuntimeInstances.Add(original);
                setup.GameplayFacts.Add(Fact(fixture, factId, fixture.TeamIds[0], fixture.Now, kind: kind));
                var unrelated = Runtime(fixture, unrelatedId, fixture.TeamIds[1], purpose);
                setup.RuntimeInstances.Add(unrelated);
                await setup.SaveChangesAsync(ct);
            }
            var outbox = new RecordingOutbox();
            if (!delayedResult)
            {
                await ProcessAsync(options, factId, outbox, ct, new FixedResultEvaluatorCatalog(new(GameplayFactResult.Correct)));
                await using var stopped = new NoCtfDbContext(options);
                var original = await stopped.RuntimeInstances.AsSplitQuery()
                    .SingleAsync(x => x.Id == originalId, ct);
                await Assert.That(original.State).IsEqualTo(RuntimeState.Stopping);
                await Assert.That(outbox.Messages.OfType<StopRuntime>().Select(x => x.RuntimeInstanceId)).IsEquivalentTo([originalId]);
                original.State = RuntimeState.Stopped;
                original.StoppedAt = fixture.Now.AddMilliseconds(500);
                await stopped.SaveChangesAsync(ct);
            }
            await using (var setup = new NoCtfDbContext(options))
            {
                var reopened = Runtime(fixture, reopenedId, fixture.TeamIds[0], purpose);
                reopened.RuntimeKind = runtimeKind;
                reopened.CreatedAt = fixture.Now.AddSeconds(1);
                reopened.RunningAt = reopened.CreatedAt;
                setup.RuntimeInstances.Add(reopened);
                await setup.SaveChangesAsync(ct);
            }
            // A delayed result (or redelivery of the completed fact) must not target the new UUID.
            await ProcessAsync(options, factId, outbox, ct, new FixedResultEvaluatorCatalog(new(GameplayFactResult.Correct)));
            await using (var verify = new NoCtfDbContext(options))
            {
                await Assert.That((await verify.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == reopenedId, ct)).State).IsEqualTo(RuntimeState.Running);
                await Assert.That((await verify.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == unrelatedId, ct)).State).IsEqualTo(RuntimeState.Running);
                await Assert.That((await verify.GameplayFacts.SingleAsync(x => x.Id == factId, ct)).Result).IsEqualTo(GameplayFactResult.Correct);
            }
            await ReconcileAsync(options, outbox, fixture.Now.AddSeconds(10), ct);
            await ReconcileAsync(options, outbox, fixture.Now.AddSeconds(11), ct);
            await using (var verify = new NoCtfDbContext(options))
                await Assert.That((await verify.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == reopenedId, ct)).State).IsEqualTo(RuntimeState.Running);
            await Assert.That(outbox.Messages.OfType<StopRuntime>().Any(x => x.RuntimeInstanceId == reopenedId)).IsFalse();

            await ReconcileAsync(options, outbox, fixture.Now.AddMinutes(31), ct);
            await ReconcileAsync(options, outbox, fixture.Now.AddMinutes(32), ct);
            await using (var verify = new NoCtfDbContext(options))
                await Assert.That((await verify.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == reopenedId, ct)).State).IsEqualTo(RuntimeState.Stopping);
            await Assert.That(outbox.Messages.OfType<StopRuntime>().Count(x => x.RuntimeInstanceId == reopenedId)).IsEqualTo(1);
        });
    }

    [Test]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player)]
    [Arguments(GameMode.Awd, GameplayFactKind.FlagAttempt, RuntimePurpose.Player)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack)]
    [Timeout(300_000)]
    public async Task Reconciliation_stops_only_modes_whose_correct_result_ends_the_runtime(
        GameMode mode, GameplayFactKind kind, RuntimePurpose purpose, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync("noctf_current_runtime", ct);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, ct, mode);
            var runtimeId = Guid.NewGuid();
            var factId = Guid.NewGuid();
            await using (var setup = new NoCtfDbContext(options))
            {
                setup.RuntimeInstances.Add(Runtime(fixture, runtimeId, fixture.TeamIds[0], purpose));
                setup.GameplayFacts.Add(Fact(fixture, factId, fixture.TeamIds[0], fixture.Now, kind: kind));
                await setup.SaveChangesAsync(ct);
            }
            var outbox = new RecordingOutbox();
            await ProcessAsync(options, factId, outbox, ct, new FixedResultEvaluatorCatalog(new(GameplayFactResult.Wrong)));
            await Assert.That(outbox.Messages.OfType<StopSolvedChallengeRuntimes>().Any()).IsFalse();
            await ReconcileAsync(options, outbox, fixture.Now.AddSeconds(1), ct);
            await using (var verify = new NoCtfDbContext(options))
            {
                await Assert.That((await verify.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == runtimeId, ct)).State).IsEqualTo(RuntimeState.Running);
                // Model recovery of a persisted correct result whose stop command was not yet applied.
                var fact = await verify.GameplayFacts.SingleAsync(x => x.Id == factId, ct);
                fact.Result = GameplayFactResult.Correct;
                await verify.SaveChangesAsync(ct);
            }
            await ReconcileAsync(options, outbox, fixture.Now.AddSeconds(2), ct);
            await ReconcileAsync(options, outbox, fixture.Now.AddSeconds(3), ct);
            await using var final = new NoCtfDbContext(options);
            var stopOnSolve = mode is GameMode.Ctf or GameMode.Awdp;
            await Assert.That((await final.RuntimeInstances.AsSplitQuery().SingleAsync(x => x.Id == runtimeId, ct)).State)
                .IsEqualTo(stopOnSolve ? RuntimeState.Stopping : RuntimeState.Running);
            await Assert.That(outbox.Messages.OfType<StopRuntime>().Count(x => x.RuntimeInstanceId == runtimeId))
                .IsEqualTo(stopOnSolve ? 1 : 0);
        });
    }

    private static async Task ReconcileAsync(DbContextOptions<NoCtfDbContext> options, RecordingOutbox outbox,
        DateTimeOffset now, CancellationToken ct)
    {
        var capacity = Substitute.For<IRunnerCapacityGate>();
        capacity.GetHeartbeatAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(RunnerHeartbeatStatus.Online);
        capacity.GetPoolInventoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RunnerPoolInventory(RunnerPoolInventoryAvailability.Available, []));
        var placement = Substitute.For<IRuntimePlacementPolicy>();
        placement.Resolve(Arg.Any<RuntimeKind>()).Returns(new RuntimePlacement(RuntimeProvider.Docker, "default"));
        await using var db = new NoCtfDbContext(options);
        await BackendMessageOperations.ExecuteRunnerAssignmentReconciliationAsync(new ReconcileRunnerAssignments(now),
            db, capacity, placement, outbox, TimeProvider.System, ct);
    }

    [Test]
    [Arguments(GameMode.Ctf, GameplayFactKind.FlagAttempt, RuntimePurpose.Player)]
    [Arguments(GameMode.Awdp, GameplayFactKind.BreakAttempt, RuntimePurpose.AwdpAttack)]
    [Timeout(300_000)]
    public async Task Correct_flag_stops_only_the_corresponding_attack_runtime(
        GameMode mode,
        GameplayFactKind kind,
        RuntimePurpose purpose,
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                $"noctf_fact_stop_{mode.ToString().ToLowerInvariant()}", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, cancellationToken, mode);
            var factId = Guid.CreateVersion7(fixture.Now);
            var attackRuntimeId = Guid.CreateVersion7(fixture.Now.AddMilliseconds(1));
            var unrelatedRuntimeId = Guid.CreateVersion7(fixture.Now.AddMilliseconds(2));
            await using (var setup = new NoCtfDbContext(options))
            {
                setup.RuntimeInstances.Add(
                    Runtime(fixture, attackRuntimeId, fixture.TeamIds[0], purpose));
                if (mode == GameMode.Awdp)
                {
                    var defenseFactId = Guid.CreateVersion7(fixture.Now.AddMilliseconds(3));
                    var defenseFact = Fact(
                        fixture,
                        defenseFactId,
                        fixture.TeamIds[0],
                        fixture.Now.AddMilliseconds(-1),
                        GameplayFactState.Pending,
                        kind: GameplayFactKind.FixAttempt);
                    defenseFact.Value = null;
                    defenseFact.ValueSha256 = null;
                    defenseFact.ReferenceKind = GameplayFactReferenceKind.PatchUpload;
                    defenseFact.ReferenceId = Guid.CreateVersion7(fixture.Now.AddMilliseconds(4));
                    setup.GameplayFacts.Add(defenseFact);
                    setup.RuntimeInstances.Add(Runtime(
                        fixture,
                        unrelatedRuntimeId,
                        fixture.TeamIds[0],
                        RuntimePurpose.AwdpTarget,
                        defenseFactId));
                }
                setup.GameplayFacts.Add(Fact(
                    fixture,
                    factId,
                    fixture.TeamIds[0],
                    fixture.Now,
                    kind: kind));
                await setup.SaveChangesAsync(cancellationToken);
            }
            var outbox = new RecordingOutbox();
            var evaluator = new FixedResultEvaluator(GameplayFactResult.Correct);

            await ProcessAsync(
                options,
                factId,
                outbox,
                cancellationToken,
                new FixedResultEvaluatorCatalog(evaluator));

            await using var verification = new NoCtfDbContext(options);
            var runtimes = await verification.RuntimeInstances.AsNoTracking()
                .IgnoreAutoIncludes()
                .Select(item => new { item.Id, item.State })
                .ToDictionaryAsync(item => item.Id, cancellationToken);
            await Assert.That(runtimes[attackRuntimeId].State).IsEqualTo(RuntimeState.Stopping);
            if (mode == GameMode.Awdp)
                await Assert.That(runtimes[unrelatedRuntimeId].State).IsEqualTo(RuntimeState.Running);
            var stops = outbox.Messages.OfType<StopRuntime>().ToArray();
            await Assert.That(stops.Length).IsEqualTo(1);
            await Assert.That(stops[0].RuntimeInstanceId).IsEqualTo(attackRuntimeId);
            var resolutionEvents = await verification.CompetitionEvents.AsNoTracking()
                .Where(item => item.Kind == CompetitionEventKind.AwdpBreakResolved)
                .ToArrayAsync(cancellationToken);
            if (mode == GameMode.Awdp)
            {
                await Assert.That(resolutionEvents.Length).IsEqualTo(1);
                await Assert.That(resolutionEvents[0].Visibility)
                    .IsEqualTo(CompetitionEventVisibility.Public);
                await Assert.That(resolutionEvents[0].TeamId).IsEqualTo(fixture.TeamIds[0]);
                await Assert.That(resolutionEvents[0].CompetitionChallengeId)
                    .IsEqualTo(fixture.CompetitionChallengeId);
                await Assert.That(resolutionEvents[0].GameplayFactId).IsEqualTo(factId);
                await Assert.That(resolutionEvents[0].Reason).IsNull();
                await Assert.That(resolutionEvents[0].Reason ?? string.Empty).DoesNotContain(fixture.Flag);
            }
            else
                await Assert.That(resolutionEvents).IsEmpty();
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Later_delivery_processes_earlier_fact_before_same_team_resubmission(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_reverse_order", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, cancellationToken);
            var earlierId = Guid.Parse("00000000-0000-0000-0000-000000000001");
            var laterId = Guid.Parse("00000000-0000-0000-0000-000000000002");
            await AddFactsAsync(options,
            [
                Fact(fixture, earlierId, fixture.TeamIds[0], fixture.Now),
                Fact(fixture, laterId, fixture.TeamIds[0], fixture.Now.AddSeconds(1))
            ], cancellationToken);
            var outbox = new RecordingOutbox();

            await ProcessAsync(options, laterId, outbox, cancellationToken);

            await using (var afterFirst = new NoCtfDbContext(options))
            {
                var facts = await afterFirst.GameplayFacts.AsNoTracking()
                    .OrderBy(item => item.OccurredAt)
                    .ThenBy(item => item.Id)
                    .ToArrayAsync(cancellationToken);
                await Assert.That(facts[0].State).IsEqualTo(GameplayFactState.Queued);
                await Assert.That(facts[1].State).IsEqualTo(GameplayFactState.Completed);
            }

            await ProcessAsync(options, laterId, outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var results = await verification.GameplayFacts.AsNoTracking()
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .Select(item => item.Result)
                .ToArrayAsync(cancellationToken);
            await Assert.That(results.Length).IsEqualTo(2);
            await Assert.That(results[0]).IsNull();
            await Assert.That(results[1]).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(outbox.Messages.OfType<BloodAwarded>().Count()).IsEqualTo(0);
            await ProcessAsync(options, earlierId, outbox, cancellationToken);
            await Assert.That(await verification.GameplayFacts.AsNoTracking().Where(item => item.Id == earlierId)
                .Select(item => item.State).SingleAsync(cancellationToken)).IsEqualTo(GameplayFactState.Completed);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Equal_occurred_at_uses_gameplay_fact_id_as_tie_breaker(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_id_tie_break", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, cancellationToken);
            var lowerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var higherId = Guid.Parse("20000000-0000-0000-0000-000000000001");
            await AddFactsAsync(options,
            [
                Fact(fixture, higherId, fixture.TeamIds[0], fixture.Now),
                Fact(fixture, lowerId, fixture.TeamIds[0], fixture.Now)
            ], cancellationToken);
            var outbox = new RecordingOutbox();

            await ProcessAsync(options, higherId, outbox, cancellationToken);
            await ProcessAsync(options, lowerId, outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var facts = await verification.GameplayFacts.AsNoTracking()
                .OrderBy(item => item.Id)
                .ToArrayAsync(cancellationToken);
            await Assert.That(facts[0].Id).IsEqualTo(lowerId);
            await Assert.That(facts[0].Result).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(facts[1].Id).IsEqualTo(higherId);
            await Assert.That(facts[1].Result).IsEqualTo(GameplayFactResult.Correct);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Concurrent_cross_team_delivery_completes_each_fact_once_and_emits_one_adjudication(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_blood_order", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 3, cancellationToken);
            var factIds = new[]
            {
                Guid.Parse("30000000-0000-0000-0000-000000000001"),
                Guid.Parse("30000000-0000-0000-0000-000000000002"),
                Guid.Parse("30000000-0000-0000-0000-000000000003")
            };
            await AddFactsAsync(options,
                factIds.Select((id, index) => Fact(
                    fixture,
                    id,
                    fixture.TeamIds[index],
                    fixture.Now.AddMilliseconds(index))).ToArray(),
                cancellationToken);
            var outbox = new RecordingOutbox();

            await Task.WhenAll(factIds.Select(id =>
                ProcessAsync(options, id, outbox, cancellationToken)));
            await ProcessAsync(options, factIds[1], outbox, cancellationToken);
            await ProcessAsync(options, factIds[2], outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var facts = await verification.GameplayFacts.AsNoTracking()
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.Id)
                .ToArrayAsync(cancellationToken);
            await Assert.That(facts.Length).IsEqualTo(3);
            await Assert.That(facts.All(item => item.Result == GameplayFactResult.Correct))
                .IsTrue();
            await Assert.That(outbox.Messages.OfType<GameplayFactStateChanged>().Count())
                .IsEqualTo(3);
            await Assert.That(await verification.CompetitionEvents.AsNoTracking()
                .CountAsync(item => item.Kind == CompetitionEventKind.GameplayFactAdjudicated, cancellationToken))
                .IsEqualTo(3);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Blackout_solve_is_scored_without_blood_event_or_broadcast(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_blackout_blood", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, cancellationToken);
            await using (var configure = new NoCtfDbContext(options))
            {
                var competition = await configure.Competitions.AsSplitQuery()
                    .SingleAsync(cancellationToken);
                competition.HiddenStartAt = fixture.Now.AddSeconds(-1);
                await configure.SaveChangesAsync(cancellationToken);
            }
            var factId = Guid.Parse("32000000-0000-0000-0000-000000000001");
            await AddFactsAsync(options,
                [Fact(fixture, factId, fixture.TeamIds[0], fixture.Now.AddSeconds(-2))],
                cancellationToken);
            var outbox = new RecordingOutbox();

            await ProcessAsync(options, factId, outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var fact = await verification.GameplayFacts.AsNoTracking()
                .SingleAsync(item => item.Id == factId, cancellationToken);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(outbox.Messages.OfType<BloodAwarded>()).IsEmpty();
            var bloodEvents = await verification.CompetitionEvents.AsNoTracking()
                .CountAsync(item => item.Kind == CompetitionEventKind.FirstBloodAwarded
                    || item.Kind == CompetitionEventKind.SecondBloodAwarded
                    || item.Kind == CompetitionEventKind.ThirdBloodAwarded,
                    cancellationToken);
            await Assert.That(bloodEvents).IsEqualTo(0);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Earlier_internal_solve_is_retained_without_consuming_public_blood(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_internal_track_blood", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 2, cancellationToken);
            await using (var configure = new NoCtfDbContext(options))
            {
                var competition = configure.Competitions.AsSplitQuery().Single();
                competition.TracksEnabled = true;
                competition.Tracks = CompetitionTrackConfiguration.ToPersisted(new(
                        [
                            new("default", "Official", true, true, false, true, true, true, true, true),
                            new("internal", "Internal", false, false, true, false, false, false, false, false)
                        ]));
                configure.Teams.Single(team => team.Id == fixture.TeamIds[0]).TrackKey = "internal";
                await configure.SaveChangesAsync(cancellationToken);
            }
            var internalFactId = Guid.Parse("31000000-0000-0000-0000-000000000001");
            var publicFactId = Guid.Parse("31000000-0000-0000-0000-000000000002");
            await AddFactsAsync(options,
            [
                Fact(fixture, internalFactId, fixture.TeamIds[0], fixture.Now),
                Fact(fixture, publicFactId, fixture.TeamIds[1], fixture.Now.AddMilliseconds(1))
            ], cancellationToken);
            var outbox = new RecordingOutbox();

            await ProcessAsync(options, internalFactId, outbox, cancellationToken);
            await ProcessAsync(options, publicFactId, outbox, cancellationToken);
            await PublishBloodAsync(options, fixture.CompetitionId, outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var facts = await verification.GameplayFacts.AsNoTracking()
                .OrderBy(item => item.OccurredAt)
                .ToArrayAsync(cancellationToken);
            await Assert.That(facts.All(item => item.Result == GameplayFactResult.Correct)).IsTrue();
            var awards = outbox.Messages.OfType<BloodAwarded>().ToArray();
            await Assert.That(awards.Length).IsEqualTo(1);
            await Assert.That(awards[0].TeamId).IsEqualTo(fixture.TeamIds[1]);
            await Assert.That(awards[0].BloodRank).IsEqualTo(LeaderboardBloodRank.First);
            var eventCount = await verification.CompetitionEvents.AsNoTracking()
                .CountAsync(@event => @event.Kind == CompetitionEventKind.FirstBloodAwarded,
                    cancellationToken);
            await Assert.That(eventCount).IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Unchanged_correct_rejudge_does_not_append_another_blood_award(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_unchanged_rejudge", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 1, cancellationToken);
            var factId = Guid.Parse("40000000-0000-0000-0000-000000000001");
            await AddFactsAsync(options,
            [
                Fact(fixture, factId, fixture.TeamIds[0], fixture.Now)
            ], cancellationToken);
            var outbox = new RecordingOutbox();

            await ProcessAsync(options, factId, outbox, cancellationToken);
            await PublishBloodAsync(options, fixture.CompetitionId, outbox, cancellationToken);
            await using (var rejudge = new NoCtfDbContext(options))
            {
                await rejudge.GameplayFacts.Where(fact => fact.Id == factId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(fact => fact.State, GameplayFactState.Queued)
                        .SetProperty(fact => fact.ConcurrencyStamp, Guid.NewGuid())
                        .SetProperty(fact => fact.UpdatedAt, DateTimeOffset.UtcNow),
                        cancellationToken);
            }

            await ProcessAsync(options, factId, outbox, cancellationToken);
            await PublishBloodAsync(options, fixture.CompetitionId, outbox, cancellationToken);

            await using var verification = new NoCtfDbContext(options);
            var fact = await verification.GameplayFacts.AsNoTracking()
                .SingleAsync(item => item.Id == factId, cancellationToken);
            await Assert.That(fact.State).IsEqualTo(GameplayFactState.Completed);
            await Assert.That(fact.Result).IsEqualTo(GameplayFactResult.Correct);
            await Assert.That(outbox.Messages.OfType<BloodAwarded>().Count()).IsEqualTo(1);
            var bloodEvents = await verification.CompetitionEvents.AsNoTracking()
                .CountAsync(@event =>
                    @event.Kind == CompetitionEventKind.FirstBloodAwarded
                    || @event.Kind == CompetitionEventKind.SecondBloodAwarded
                    || @event.Kind == CompetitionEventKind.ThirdBloodAwarded,
                    cancellationToken);
            await Assert.That(bloodEvents).IsEqualTo(1);
        });
    }

    [Test]
    [Timeout(300_000)]
    public async Task Evaluation_materializes_only_relevant_stable_prior_facts(
        CancellationToken cancellationToken)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = await StartPostgresAsync(
                "noctf_fact_query_scope", cancellationToken);
            var options = Options(postgres);
            var fixture = await SeedAsync(options, 2, cancellationToken);
            var currentId = Guid.Parse("50000000-0000-0000-0000-000000000002");
            var relevantId = Guid.Parse("50000000-0000-0000-0000-000000000001");
            var otherTemplateId = Guid.CreateVersion7();
            var otherChallengeId = Guid.CreateVersion7();
            await using (var setup = new NoCtfDbContext(options))
            {
                setup.Challenges.Add(new CtfChallenge
                {
                    Id = otherTemplateId,
                    OwnerId = fixture.OwnerId,
                    Visibility = ChallengeVisibility.Private,
                    Title = "Irrelevant history challenge",
                    Direction = "Web",
                    Definition = TestConfigurations.Definition(GameMode.Ctf),
                    CreatedAt = fixture.Now,
                    UpdatedAt = fixture.Now
                });
                setup.CompetitionChallenges.Add(new CtfCompetitionChallenge
                {
                    Id = otherChallengeId,
                    CompetitionId = fixture.CompetitionId,
                    ChallengeId = otherTemplateId,
                    Order = 1,
                    IsPublished = true,
                    Rules = TestConfigurations.Rules(GameMode.Ctf),
                    UpdatedAt = fixture.Now
                });
                setup.GameplayFacts.Add(Fact(
                    fixture,
                    relevantId,
                    fixture.TeamIds[0],
                    fixture.Now.AddSeconds(-1),
                    GameplayFactState.Completed,
                    GameplayFactResult.Correct));
                setup.GameplayFacts.Add(Fact(
                    fixture,
                    Guid.CreateVersion7(),
                    fixture.TeamIds[1],
                    fixture.Now.AddSeconds(-1),
                    GameplayFactState.Completed,
                    GameplayFactResult.Correct));
                setup.GameplayFacts.Add(Fact(
                    fixture,
                    Guid.CreateVersion7(),
                    fixture.TeamIds[0],
                    fixture.Now.AddSeconds(1),
                    GameplayFactState.Completed,
                    GameplayFactResult.Correct));
                for (var index = 0; index < 1_000; index++)
                {
                    var irrelevant = Fact(
                        fixture,
                        Guid.CreateVersion7(),
                        fixture.TeamIds[index % fixture.TeamIds.Length],
                        fixture.Now.AddMinutes(-10).AddTicks(index),
                        GameplayFactState.Completed,
                        GameplayFactResult.Correct);
                    irrelevant.CompetitionChallengeId = otherChallengeId;
                    setup.GameplayFacts.Add(irrelevant);
                }
                setup.GameplayFacts.Add(Fact(
                    fixture,
                    currentId,
                    fixture.TeamIds[0],
                    fixture.Now));
                await setup.SaveChangesAsync(cancellationToken);
            }
            var evaluator = new CapturingEvaluator();
            var catalog = new CapturingEvaluatorCatalog(evaluator);

            await ProcessAsync(
                options,
                currentId,
                new RecordingOutbox(),
                cancellationToken,
                catalog);

            await Assert.That(evaluator.Context).IsNotNull();
            await Assert.That(evaluator.Context!.PriorFacts.Select(item => item.Id))
                .IsEquivalentTo([relevantId]);
        });
    }

    private static async Task ProcessAsync(
        DbContextOptions<NoCtfDbContext> options,
        Guid gameplayFactId,
        RecordingOutbox outbox,
        CancellationToken cancellationToken,
        IGameplayFactEvaluatorCatalog? evaluatorCatalog = null)
    {
        await using var db = new NoCtfDbContext(options);
        var processor = new GameplayFactProcessor(
            db,
            evaluatorCatalog ?? new GameModeGameplayFactEvaluatorCatalog(),
            outbox,
            Substitute.For<ILeaderboardSnapshotFactory>(),
            new CompetitionEventStore(db, outbox));
        var expectedStamp = await db.GameplayFacts.AsNoTracking().Where(item => item.Id == gameplayFactId)
            .Select(item => item.ConcurrencyStamp).SingleAsync(cancellationToken);
        await processor.ProcessAsync(gameplayFactId, expectedStamp, cancellationToken);
        var completed = await db.GameplayFacts.AsNoTracking().IgnoreAutoIncludes()
            .SingleAsync(item => item.Id == gameplayFactId, cancellationToken);
        await using var cleanupDb = new NoCtfDbContext(options);
        await new StopSolvedChallengeRuntimesHandler(cleanupDb, outbox,
            new CompetitionEventStore(cleanupDb, outbox), TimeProvider.System)
            .Handle(new StopSolvedChallengeRuntimes(completed.Id, completed.ConcurrencyStamp), cancellationToken);
    }

    private static async Task PublishBloodAsync(DbContextOptions<NoCtfDbContext> options,
        Guid competitionId, RecordingOutbox outgoing, CancellationToken ct)
    {
        await using var db = new NoCtfDbContext(options);
        using var services = new ServiceCollection().AddFusionCache(NoCtfCacheNames.Leaderboards)
            .Services.BuildServiceProvider();
        var cache = new FusionLeaderboardCache(db, new LeaderboardProjectionEngine(new LeaderboardProjectorCatalog()),
            new RecordingRefreshPublisher(), services.GetRequiredService<IFusionCacheProvider>());
        await cache.RefreshAsync(competitionId, ct);
        var projection = await cache.GetScoreboardAsync(competitionId, ct)
            ?? throw new InvalidOperationException("Published projection is missing.");
        await new BloodAnnouncementReconciler(db, new CompetitionEventStore(db, outgoing), outgoing, TimeProvider.System)
            .ReconcileAsync(projection, ct);
        await new BloodAnnouncementReconciler(db, new CompetitionEventStore(db, outgoing), outgoing, TimeProvider.System)
            .ReconcileAsync(projection, ct);
    }

    private sealed class RecordingRefreshPublisher : ILeaderboardRefreshPublisher
    {
        public Task PublishAsync(ScoreboardProjection projection, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static async Task<Fixture> SeedAsync(
        DbContextOptions<NoCtfDbContext> options,
        int teamCount,
        CancellationToken cancellationToken,
        GameMode mode = GameMode.Ctf)
    {
        var now = DateTimeOffset.UtcNow;
        var ownerId = Guid.CreateVersion7();
        var competitionId = Guid.CreateVersion7();
        var challengeId = Guid.CreateVersion7();
        var competitionChallengeId = Guid.CreateVersion7();
        const string flag = "flag{stable-order}";
        var teamIds = Enumerable.Range(0, teamCount)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        var memberIds = Enumerable.Range(0, teamCount)
            .Select(_ => Guid.CreateVersion7())
            .ToArray();
        await using var db = new NoCtfDbContext(options);
        await db.Database.EnsureCreatedAsync(cancellationToken);
        db.Users.Add(User(ownerId, "fact-owner", UserRole.Organizer, now));
        for (var index = 0; index < teamCount; index++)
        {
            db.Users.Add(User(memberIds[index], $"fact-member-{index}", UserRole.User, now));
            db.Teams.Add(new Team
            {
                Id = teamIds[index],
                CompetitionId = competitionId,
                Name = $"Fact Team {index}",
                CaptainId = memberIds[index],
                MemberIds = [memberIds[index]],
                InvitationToken = index.ToString().PadLeft(32, '0'),
                RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = now
            });
        }
        var competition = CompetitionGeneratedCatalog.Create(mode);
        competition.Id = competitionId;
        competition.OwnerId = ownerId;
        competition.Title = "Gameplay fact ordering";
        competition.ModeConfiguration = TestConfigurations.Competition(mode);
        competition.FlagDerivationSecret = new byte[32];
        competition.StartAt = now.AddMinutes(-5);
        competition.EndAt = now.AddHours(1);
        competition.Status = CompetitionStatus.Running;
        competition.CreatedAt = now;
        competition.UpdatedAt = now;
        db.Competitions.Add(competition);
        var challenge = ChallengeGeneratedCatalog.Create(mode);
        challenge.Id = challengeId;
        challenge.OwnerId = ownerId;
        challenge.Visibility = ChallengeVisibility.Private;
        challenge.Title = "Stable challenge";
        challenge.Direction = "Web";
        challenge.Definition = TestConfigurations.Definition(mode);
        challenge.CreatedAt = now;
        challenge.UpdatedAt = now;
        db.Challenges.Add(challenge);
        var competitionChallenge = CompetitionChallengeGeneratedCatalog.Create(mode);
        competitionChallenge.Id = competitionChallengeId;
        competitionChallenge.CompetitionId = competitionId;
        competitionChallenge.ChallengeId = challengeId;
        competitionChallenge.IsPublished = true;
        competitionChallenge.Rules = TestConfigurations.Rules(mode);
        competitionChallenge.UpdatedAt = now;
        db.CompetitionChallenges.Add(competitionChallenge);
        var challengeFlag = ChallengeFlagGeneratedCatalog.Create(
            mode == GameMode.Awdp ? ChallengeFlagType.Team : ChallengeFlagType.Competition);
        challengeFlag.Id = Guid.CreateVersion7();
        challengeFlag.CompetitionChallengeId = competitionChallengeId;
        challengeFlag.TeamId = mode == GameMode.Awdp ? teamIds[0] : null;
        challengeFlag.Flag = flag;
        challengeFlag.FlagSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(flag));
        challengeFlag.CreatedAt = now;
        db.ChallengeFlags.Add(challengeFlag);
        await db.SaveChangesAsync(cancellationToken);
        return new(
            now,
            ownerId,
            competitionId,
            challengeId,
            competitionChallengeId,
            teamIds,
            memberIds,
            flag);
    }

    private static async Task AddFactsAsync(
        DbContextOptions<NoCtfDbContext> options,
        IReadOnlyList<GameplayFact> facts,
        CancellationToken cancellationToken)
    {
        await using var db = new NoCtfDbContext(options);
        db.GameplayFacts.AddRange(facts);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static GameplayFact Fact(
        Fixture fixture,
        Guid id,
        Guid teamId,
        DateTimeOffset occurredAt,
        GameplayFactState state = GameplayFactState.Queued,
        GameplayFactResult? result = null,
        GameplayFactKind kind = GameplayFactKind.FlagAttempt)
    {
        var fact = GameplayFactGeneratedCatalog.Create(kind);
        fact.Id = id;
        fact.CompetitionId = fixture.CompetitionId;
        fact.CompetitionChallengeId = fixture.CompetitionChallengeId;
        fact.TeamId = teamId;
        fact.ActorUserId = fixture.MemberIds[Array.IndexOf(fixture.TeamIds, teamId)];
        fact.Value = fixture.Flag;
        fact.ValueSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(fixture.Flag));
        fact.OccurredAt = occurredAt;
        fact.State = state;
        fact.Result = result;
        fact.UpdatedAt = occurredAt;
        return fact;
    }

    private static RuntimeInstance Runtime(
        Fixture fixture,
        Guid id,
        Guid teamId,
        RuntimePurpose purpose,
        Guid? gameplayFactId = null)
    {
        var runtime = RuntimeInstanceGeneratedCatalog.Create(purpose);
        runtime.Id = id;
        runtime.CompetitionId = fixture.CompetitionId;
        runtime.CompetitionChallengeId = fixture.CompetitionChallengeId;
        runtime.TeamId = teamId;
        runtime.GameplayFactId = gameplayFactId;
        runtime.RuntimeKind = RuntimeKind.Container;
        runtime.RuntimeProvider = RuntimeProvider.Docker;
        runtime.RunnerId = "runner-fact-tests";
        runtime.State = RuntimeState.Running;
        runtime.ProviderReceipt = RuntimeReceiptTestData.ContainerEntity();
        runtime.CreatedAt = fixture.Now.AddMinutes(-1);
        runtime.RunningAt = fixture.Now.AddMinutes(-1);
        runtime.ExpiresAt = fixture.Now.AddMinutes(30);
        return runtime;
    }

    private static User User(
        Guid id,
        string userName,
        UserRole role,
        DateTimeOffset now) =>
        new()
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            PasswordHash = "test",
            Kind = UserKind.Human,
            Role = role,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static async Task<PostgreSqlContainer> StartPostgresAsync(
        string database,
        CancellationToken cancellationToken)
    {
        var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
            .WithDatabase(database)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await postgres.StartAsync(cancellationToken);
        return postgres;
    }

    private static DbContextOptions<NoCtfDbContext> Options(PostgreSqlContainer postgres) =>
        new DbContextOptionsBuilder<NoCtfDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Throw(
                RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;

    private sealed class RecordingOutbox : IPostCommitMessagePublisher
    {
        private readonly AsyncLocal<List<object>?> pending = new();
        public ConcurrentQueue<object> Messages { get; } = [];

        public ValueTask PublishAsync<T>(T message)
        {
            (pending.Value ??= []).Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask ScheduleAsync<T>(T message, DateTimeOffset scheduledAt) =>
            ValueTask.CompletedTask;

        public ValueTask PublishToRunnerNodeAsync<T>(T message)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public ValueTask ScheduleToRunnerNodeAsync<T>(T message, DateTimeOffset scheduledAt)
            where T : NoCTF.Application.Runtime.Instances.IRunnerNodeMessage =>
            ValueTask.CompletedTask;

        public Task FlushOutgoingMessagesAsync()
        {
            foreach (var message in pending.Value ?? [])
                Messages.Enqueue(message);
            pending.Value = null;
            return Task.CompletedTask;
        }

        public Task FlushCommittedMessagesAsync() => FlushOutgoingMessagesAsync();

        public void DiscardPendingMessages() => pending.Value = null;
    }

    private sealed class CapturingEvaluatorCatalog(CapturingEvaluator evaluator)
        : IGameplayFactEvaluatorCatalog
    {
        public IGameplayFactEvaluator Get(GameMode mode) => evaluator;
    }

    private sealed class CapturingEvaluator : IGameplayFactEvaluator
    {
        public GameplayFactProcessingContext? Context { get; private set; }

        public GameplayFactDecision Evaluate(GameplayFactProcessingContext context)
        {
            Context = context;
            return new(GameplayFactResult.Wrong, null, context.GameplayFact.OccurredAt);
        }
    }

    private sealed class FixedResultEvaluatorCatalog(FixedResultEvaluator evaluator)
        : IGameplayFactEvaluatorCatalog
    {
        public IGameplayFactEvaluator Get(GameMode mode) => evaluator;
    }

    private sealed class FixedResultEvaluator(GameplayFactResult result)
        : IGameplayFactEvaluator
    {
        public GameplayFactDecision Evaluate(GameplayFactProcessingContext context) =>
            new(result, null, context.GameplayFact.OccurredAt);
    }

    private sealed record Fixture(
        DateTimeOffset Now,
        Guid OwnerId,
        Guid CompetitionId,
        Guid ChallengeId,
        Guid CompetitionChallengeId,
        Guid[] TeamIds,
        Guid[] MemberIds,
        string Flag);
}
