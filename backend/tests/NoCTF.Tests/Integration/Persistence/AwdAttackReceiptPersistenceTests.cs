using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.GameModes.Awd.Configuration;
using NoCTF.GameModes.GameplayFact;
using NoCTF.GameModes.Registration;
using NoCTF.Infrastructure.Competitions.Awd;
using NoCTF.Infrastructure.GameplayFacts.Intake;
using NoCTF.Infrastructure.GameplayFacts.Processing;
using NoCTF.Infrastructure.Persistence;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

public sealed partial class AwdRoundCoordinationTests
{
    [Test]
    [Timeout(300_000)]
    public async Task Cross_round_capture_uses_durable_receipt_and_flag_identity_after_competition_finishes(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_attack_receipts").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var competition = await db.Competitions.SingleAsync(ct);
            ((AwdCompetitionModeConfiguration)competition.ModeConfiguration!).AwdOptions = new()
                { FlagsPerStore = 2, RoundGapSeconds = 30 };
            var attackers = Enumerable.Range(1, 2).Select(index =>
            {
                var userId = Guid.NewGuid();
                db.Users.Add(new User { Id = userId, UserName = $"attacker{index}", NormalizedUserName = $"ATTACKER{index}",
                    Email = $"attacker{index}@example.test", PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
                var team = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, CaptainId = userId,
                    MemberIds = [userId], Name = $"Attacker {index}", InvitationToken = index.ToString().PadLeft(32, 'a'),
                    RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now };
                db.Teams.Add(team);
                return team;
            }).ToArray();
            await db.SaveChangesAsync(ct);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            await coordinator.GenerateFlagsAsync(outbox.Published.OfType<NoCTF.Application.Messaging.GenerateAwdFlags>().Last(), ct);
            var flags = await db.ChallengeFlags.AsNoTracking().Where(flag => flag.TeamId == fixture.TeamId)
                .OrderBy(flag => flag.Id).ToArrayAsync(ct);
            await Assert.That(flags.Length).IsEqualTo(2);
            await Assert.That(flags.All(flag => flag.AwdIssue!.DeliveryState == AwdFlagDeliveryState.Pending)).IsTrue();
            var firstRound = await db.AwdRounds.AsNoTracking().SingleAsync(ct);

            async Task<NoCTF.Application.Common.OperationResult<IReadOnlyList<GameplayFactAccepted>, GameplayFactAdmissionFailureCode>> Submit(
                Team team, params string[] values)
            {
                await using var intakeDb = new NoCtfDbContext(options);
                var intake = new GameplayFactIntakeStore(intakeDb, new RecordingOutbox(),
                    clock: clock);
                return await new SubmitFlag(intake, new GameModeGameplayFactAdmissionPolicy()).ExecuteBatchAsync(
                    fixture.CompetitionId, fixture.CompetitionChallengeId, team.CaptainId, values, clock.UtcNow, ct);
            }

            clock.UtcNow = fixture.Now.AddMinutes(5).AddSeconds(5);
            var gap = await Submit(attackers[0], flags[0].Flag);
            await Assert.That(gap.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.AwdRoundGap);
            clock.UtcNow = fixture.Now.AddMinutes(5).AddSeconds(31);
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var first = await Submit(attackers[0], flags[0].Flag);
            await Assert.That(first.Succeeded).IsTrue();
            clock.UtcNow = fixture.Now.AddMinutes(11).AddSeconds(1);
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var repeatedAndSecond = await Submit(attackers[0], flags[0].Flag, flags[1].Flag);
            var otherAttacker = await Submit(attackers[1], flags[0].Flag);
            await Assert.That(repeatedAndSecond.Succeeded).IsTrue();
            await Assert.That(otherAttacker.Succeeded).IsTrue();
            var receipts = await db.GameplayFacts.AsNoTracking().Include(fact => fact.Awd).OrderBy(fact => fact.OccurredAt).ToArrayAsync(ct);
            await Assert.That(receipts.Length).IsEqualTo(4);
            await Assert.That(receipts[0].Awd!.ReceivedRoundNumber).IsEqualTo(2);
            await Assert.That(receipts[0].Awd!.ReceivedActiveTicks).IsEqualTo(TimeSpan.FromSeconds(901).Ticks);
            await Assert.That(receipts.Skip(1).All(fact => fact.Awd!.ReceivedRoundNumber == 3)).IsTrue();

            // A queue delay, shutdown and later challenge timing edit cannot change receipt eligibility.
            await db.Competitions.ExecuteUpdateAsync(update => update.SetProperty(item => item.Status, CompetitionStatus.Finished), ct);
            await db.CompetitionChallenges.ExecuteUpdateAsync(update => update.SetProperty(item => item.ScoringEndsAt, fixture.Now), ct);
            async Task Process(Guid id)
            {
                await using var workerDb = new NoCtfDbContext(options);
                var processor = new GameplayFactProcessor(workerDb, new GameModeGameplayFactEvaluatorCatalog(),
                    new RecordingOutbox(), new TestDbContextFactory(workerDb), new NoCTF.Persistence.PostgreSql.PostgreSqlConstraintClassifier());
                var expectedStamp = await workerDb.GameplayFacts.AsNoTracking().Where(item => item.Id == id)
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct);
                await processor.ProcessAsync(id, expectedStamp, ct);
            }
            await Task.WhenAll(receipts.Select(fact => Process(fact.Id)));
            // Dispatch any next-in-scope wakeups that the recording message publisher did not deliver.
            foreach (var receipt in receipts) await Process(receipt.Id);
            db.ChangeTracker.Clear();
            var completed = await db.GameplayFacts.AsNoTracking().Include(fact => fact.Awd).ToArrayAsync(ct);
            await Assert.That(completed.Count(fact => fact.Result == GameplayFactResult.Correct)).IsEqualTo(3);
            await Assert.That(completed.Count(fact => fact.Result == GameplayFactResult.Duplicate)).IsEqualTo(1);
            await Assert.That(completed.All(fact => fact.Awd!.GenerationRoundId == firstRound.Id)).IsTrue();
            await Assert.That(completed.All(fact => fact.Awd!.Settlement == AwdSettlementState.Final)).IsTrue();
            await Assert.That(await db.AwdCaptureIdentities.CountAsync(ct)).IsEqualTo(3);
            var acceptedFact = completed.First(fact => fact.Result == GameplayFactResult.Correct && fact.TeamId == attackers[0].Id);
            var status = await new NoCTF.Infrastructure.GameplayFacts.Status.GameplayFactStatusReader(db)
                .FindAsync(fixture.CompetitionId, acceptedFact.Id, attackers[0].CaptainId, ct);
            await Assert.That(status!.AwdAttack!.GenerationRoundNumber).IsEqualTo(1);
            await Assert.That(status.AwdAttack.ReceivedRoundNumber!.Value).IsGreaterThan(1);
            await Assert.That(status.AwdAttack.StoreId).IsNotNull();
            var response = NoCTF.API.Endpoints.GameplayFacts.GameplayFactMapper.ToStatusResponse(status);
            await Assert.That(response.AwdAttack!.GenerationRoundNumber).IsEqualTo(1);
            var closed = await Submit(attackers[0], flags[1].Flag);
            await Assert.That(closed.FailureCode).IsEqualTo(GameplayFactAdmissionFailureCode.CompetitionFinished);
        });
    }
}
