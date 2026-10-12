using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Messaging;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Koh.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.Competitions.Events;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(300_000)]
    public async Task Match_deadline_preserves_gaps_and_closes_tail_at_the_actual_boundary(bool byRounds, CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_deadline").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var competition = await db.Competitions.SingleAsync(ct);
            var configuration = (AwdCompetitionModeConfiguration)competition.ModeConfiguration!;
            configuration.RoundDurationSeconds = 60;
            configuration.AwdOptions = new() { RoundGapSeconds = 30, TotalRounds = byRounds ? 3 : null, SubmissionTailSeconds = 15 };
            competition.EndAt = fixture.Now.AddSeconds(125);
            var attackerUserId = Guid.NewGuid();
            db.Users.Add(new NoCTF.Domain.Identity.User { Id = attackerUserId, UserName = "tail-attacker",
                NormalizedUserName = "TAIL-ATTACKER", Email = "tail@example.test", PasswordHash = "test",
                CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
            var attacker = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId,
                CaptainId = attackerUserId, MemberIds = [attackerUserId], Name = "Tail attacker",
                InvitationToken = new string('b', 32), RegistrationStatus = TeamRegistrationStatus.Approved,
                RegisteredAt = fixture.Now };
            db.Teams.Add(attacker);
            await db.SaveChangesAsync(ct);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            await coordinator.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            var flag = await db.ChallengeFlags.AsNoTracking().SingleAsync(x => x.TeamId == fixture.TeamId, ct);
            var lifecycle = new CompetitionLifecycleStore(db, null!, outbox,
                new CompetitionEventStore(db, outbox), clock: clock);
            clock.UtcNow = fixture.Now.AddSeconds(90);
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            // A pause inside round two extends both the current round and the match boundary.
            clock.UtcNow = fixture.Now.AddSeconds(100);
            await lifecycle.TryTransitionWithAuditAsync(fixture.CompetitionId, CompetitionStatus.Running,
                CompetitionStatus.Paused, fixture.OwnerId, "pause", false, CompetitionLifecycleEffects.None, ct);
            clock.UtcNow = fixture.Now.AddSeconds(110);
            await lifecycle.TryTransitionWithAuditAsync(fixture.CompetitionId, CompetitionStatus.Paused,
                CompetitionStatus.Running, fixture.OwnerId, "resume", false, CompetitionLifecycleEffects.None, ct);
            clock.UtcNow = fixture.Now.AddSeconds(190);
            await Assert.That(await lifecycle.GetDueAsync(clock.UtcNow, ct)).IsEmpty();
            var source = new ClusterScheduleSource(db, new AwdRoundConfigurationCatalog(), new KohProducerConfigurationCatalog(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterScheduleSource>.Instance, []);
            await Assert.That((await source.RebuildAsync(clock.UtcNow, ct)).Any(x => x.Kind == ClusterScheduleKind.AwdRound)).IsTrue();
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            await coordinator.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            var last = await db.AwdRounds.SingleAsync(x => x.Number == 3, ct);
            var expectedEnd = fixture.Now.AddSeconds(byRounds ? 250 : 195);
            await Assert.That(last.EndsAt).IsEqualTo(expectedEnd);
            await Assert.That(last.Partial).IsEqualTo(!byRounds);
            await Assert.That(await db.AwdFlagIssues.CountAsync(x => x.RoundNumber == 3, ct)).IsEqualTo(2);
            clock.UtcNow = expectedEnd.AddSeconds(5);
            var due = (await lifecycle.GetDueAsync(clock.UtcNow, ct)).Single();
            await Assert.That(due.EndTime).IsEqualTo(expectedEnd);
            await new AdvanceCompetitionLifecycleUseCase(lifecycle).ExecuteAsync(clock.UtcNow, ct);
            var finishedAt = await db.CompetitionEvents.Where(x => x.CompetitionStatus == CompetitionStatus.Finished)
                .Select(x => x.OccurredAt).SingleAsync(ct);
            await Assert.That(finishedAt).IsEqualTo(expectedEnd);

            async Task<NoCTF.Application.Common.OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>> Submit()
            {
                await using var intakeDb = new NoCtfDbContext(options);
                var intake = new GameplayFactIntakeStore(intakeDb, new RecordingOutbox(), new GameplayFactAttemptCriticalSection(), clock: clock);
                return await new SubmitFlag(intake, new GameModeGameplayFactAdmissionPolicy()).ExecuteAsync(
                    new(fixture.CompetitionId, fixture.CompetitionChallengeId, attackerUserId, flag.Flag, clock.UtcNow), ct);
            }
            var accepted = await Submit();
            await Assert.That(accepted.Succeeded).IsTrue();
            var fact = await db.GameplayFacts.AsNoTracking().Include(x => x.Awd).SingleAsync(x => x.Kind == GameplayFactKind.FlagAttempt, ct);
            await Assert.That(fact.Awd!.ReceivedRoundId).IsEqualTo(last.Id);
            await Assert.That(fact.Awd.ReceivedActiveTicks).IsEqualTo(last.ActiveEndTicks);
            configuration.AwdOptions = configuration.AwdOptions with { SubmissionTailSeconds = 900 };
            await db.SaveChangesAsync(ct);
            var unchangedDeadline = await AwdCompetitionDeadline.ReadAsync(db, fixture.CompetitionId,
                competition.StartAt, competition.EndAt, configuration, clock.UtcNow, ct);
            await Assert.That(unchangedDeadline.AcceptanceEndsAt).IsEqualTo(expectedEnd.AddSeconds(15));
            await Assert.That(last.SubmissionTailSeconds).IsEqualTo(15);
            clock.UtcNow = expectedEnd.AddSeconds(15);
            await Assert.That((await Submit()).FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.CompetitionFinished);
            await using var workerDb = new NoCtfDbContext(options);
            var processor = new GameplayFactProcessor(workerDb, new GameModeGameplayFactEvaluatorCatalog(),
                new GameModeGameplayFactAdmissionPolicy(), new RecordingOutbox(), Substitute.For<ILeaderboardSnapshotFactory>());
            await processor.ProcessAsync(fact.Id, ct);
            await Assert.That((await workerDb.GameplayFacts.AsNoTracking().SingleAsync(x => x.Id == fact.Id, ct)).Result)
                .IsEqualTo(GameplayFactResult.Correct);
        });
    }
}
