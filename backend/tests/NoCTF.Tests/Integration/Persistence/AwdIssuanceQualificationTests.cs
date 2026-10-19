using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Application.Messaging;
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
    public async Task Issuance_keeps_track_qualification_after_track_changes_and_excludes_internal_captures(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193")
                .WithDatabase("noctf_awd_qualification").WithUsername("postgres").WithPassword("postgres").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            var fixture = await SeedAsync(options, ct);
            await using var db = new NoCtfDbContext(options);
            var competition = await db.Competitions.SingleAsync(ct);
            competition.TracksEnabled = true;
            var publicTrack = CompetitionTrackConfiguration.DefaultFor(GameMode.Awd).DefaultTrack;
            competition.Tracks = CompetitionTrackConfiguration.ToPersisted(new([publicTrack,
                publicTrack with { Key = "internal", Name = "Internal", IsDefault = false, IsInternal = true,
                    EarnsScore = false, AffectsCompetitiveResults = false }]), competition.Id);
            var teams = Enumerable.Range(0, 2).Select(index =>
            {
                var userId = Guid.NewGuid();
                db.Users.Add(new User { Id = userId, UserName = $"qualification{index}", NormalizedUserName = $"QUALIFICATION{index}",
                    Email = $"qualification{index}@example.test", PasswordHash = "test", CreatedAt = fixture.Now, UpdatedAt = fixture.Now });
                var team = new Team { Id = Guid.NewGuid(), CompetitionId = fixture.CompetitionId, CaptainId = userId,
                    MemberIds = [userId], Name = $"Qualification {index}", InvitationToken = index.ToString().PadLeft(32, 'q'),
                    RegistrationStatus = TeamRegistrationStatus.Approved, RegisteredAt = fixture.Now,
                    TrackKey = index == 0 ? publicTrack.Key : "internal" };
                db.Teams.Add(team);
                return team;
            }).ToArray();
            await db.SaveChangesAsync(ct);
            var clock = new MutableTimeProvider(fixture.Now);
            var outbox = new RecordingOutbox();
            var coordinator = new AwdRoundCoordinator(db, new AwdRoundConfigurationCatalog(), outbox, clock, Secrets());
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            var generate = outbox.Published.OfType<GenerateAwdFlags>().Last();
            await coordinator.GenerateFlagsAsync(generate, ct);
            db.ChangeTracker.Clear();
            var originalRules = await db.AwdRoundRules.AsNoTracking().SingleAsync(ct);
            await Assert.That(originalRules.EligibleTeamCount).IsEqualTo(2);
            await Assert.That(originalRules.Teams.Count).IsEqualTo(3);
            await Assert.That(originalRules.Teams.Single(x => x.TeamId == teams[1].Id).Competitive).IsFalse();
            var flags = await db.ChallengeFlags.AsNoTracking().ToArrayAsync(ct);
            await Assert.That(flags.Length).IsEqualTo(3);
            var defenderFlag = flags.Single(x => x.TeamId == fixture.TeamId);
            var internalFlag = flags.Single(x => x.TeamId == teams[1].Id);

            // Changing tracks after issuance cannot make an old internal flag competitive or erase old qualification.
            await db.Teams.Where(x => x.Id == teams[0].Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.TrackKey, "internal"), ct);
            await db.Teams.Where(x => x.Id == teams[1].Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.TrackKey, publicTrack.Key), ct);
            clock.UtcNow = fixture.Now.AddSeconds(10);
            await coordinator.GenerateFlagsAsync(generate, ct);
            await Assert.That(await db.AwdRoundRules.CountAsync(ct)).IsEqualTo(1);
            async Task<GameplayFact> SubmitAndProcess(Team team, string flag)
            {
                await using var intakeDb = new NoCtfDbContext(options);
                var intake = new GameplayFactIntakeStore(intakeDb, new RecordingOutbox(), clock: clock);
                var accepted = await new SubmitFlag(intake, new GameModeGameplayFactAdmissionPolicy()).ExecuteBatchAsync(
                    fixture.CompetitionId, fixture.CompetitionChallengeId, team.CaptainId, [flag], clock.UtcNow, ct);
                await Assert.That(accepted.Succeeded).IsTrue();
                var factId = accepted.Value!.Single().GameplayFactId;
                await using var workerDb = new NoCtfDbContext(options);
                var processor = new GameplayFactProcessor(workerDb, new GameModeGameplayFactEvaluatorCatalog(),
                    new RecordingOutbox(), Substitute.For<ILeaderboardSnapshotFactory>());
                var expectedStamp = await workerDb.GameplayFacts.AsNoTracking().Where(item => item.Id == factId)
                    .Select(item => item.ConcurrencyStamp).SingleAsync(ct);
                await processor.ProcessAsync(factId, expectedStamp, ct);
                return await workerDb.GameplayFacts.AsNoTracking().Include(x => x.Awd).SingleAsync(x => x.Id == factId, ct);
            }
            var qualified = await SubmitAndProcess(teams[0], defenderFlag.Flag);
            await Assert.That(qualified.Result).IsEqualTo(GameplayFactResult.Correct);
            var internalAttacker = await SubmitAndProcess(teams[1], defenderFlag.Flag);
            await Assert.That(internalAttacker.Awd!.AttackOutcome).IsEqualTo(AwdAttackOutcome.NotScorable);
            var internalTarget = await SubmitAndProcess(teams[0], internalFlag.Flag);
            await Assert.That(internalTarget.Awd!.AttackOutcome).IsEqualTo(AwdAttackOutcome.NotScorable);
            await Assert.That(await db.AwdCaptureIdentities.CountAsync(ct)).IsEqualTo(1);

            clock.UtcNow = fixture.Now.AddMinutes(5).AddSeconds(1);
            await coordinator.AdvanceAsync(new(fixture.CompetitionId, clock.UtcNow), ct);
            await coordinator.GenerateFlagsAsync(outbox.Published.OfType<GenerateAwdFlags>().Last(), ct);
            var nextRules = await db.AwdRoundRules.AsNoTracking().SingleAsync(x => x.Id != originalRules.Id, ct);
            await Assert.That(nextRules.Teams.Single(x => x.TeamId == teams[0].Id).Competitive).IsFalse();
            await Assert.That(nextRules.Teams.Single(x => x.TeamId == teams[1].Id).Competitive).IsTrue();
            await Assert.That(nextRules.EligibleTeamCount).IsEqualTo(2);
        });
    }
}
