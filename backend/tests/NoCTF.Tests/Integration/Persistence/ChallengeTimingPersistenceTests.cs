using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Challenges.Management;
using NoCTF.Infrastructure.Challenges.Timing;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ChallengeTimingPersistenceTests
{
    [Test, Timeout(300_000)]
    public Task Opening_waits_for_resume_and_a_canceled_or_stale_plan_cannot_publish(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now, f.Now.AddMinutes(30), f.Now.AddHours(1));
        await Assert.That(await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct)).IsNull();
        var revision = f.Challenge.TimingRevision;
        f.Competition.Status = CompetitionStatus.Paused; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        f.Competition.Status = CompetitionStatus.Running; await f.Db.SaveChangesAsync(ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now, ct);
        await Assert.That(f.Challenge.IsPublished).IsTrue();
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddSeconds(5), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(f.Now);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing with { AutoOpenAt = f.Now.AddMinutes(5) }, f.Now), ct);
        await f.Store.OpenAsync(new(f.Challenge.Id, revision), f.Now.AddMinutes(10), ct);
        await Assert.That(f.Challenge.IsPublished).IsFalse();
        var schedules = await new ChallengeTimingScheduleSource(f.Db).RebuildAsync(f.Now, ct);
        await Assert.That(schedules.Any(x => x.Message is AdvanceChallengeOpening opening && opening.TimingRevision == f.Challenge.TimingRevision)).IsTrue();
    });

    [Test, Timeout(300_000)]
    public Task Latest_time_rules_reclassify_existing_correct_and_wrong_facts_without_new_attempts_or_checker_execution(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var at = f.Now.AddMinutes(-10);
        var correct = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Correct, Value = "synthetic" };
        var wrong = new FlagAttemptGameplayFact { Id = Guid.NewGuid(), CompetitionId = f.Competition.Id,
            CompetitionChallengeId = f.Challenge.Id, OccurredAt = at, UpdatedAt = at,
            State = GameplayFactState.Completed, Result = GameplayFactResult.Wrong, Value = "wrong" };
        f.Db.AddRange(correct, wrong); await f.Db.SaveChangesAsync(ct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(-1)), f.Now), ct);
        var stale = f.Challenge.TimingRevision;
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(wrong.Result).IsEqualTo(GameplayFactResult.Wrong);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(null, at.AddMinutes(1)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, stale), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.Correct);
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, new(at.AddMinutes(1), at.AddMinutes(2)), f.Now), ct);
        await f.Store.RecalculateAsync(new(f.Challenge.Id, f.Challenge.TimingRevision), f.Now, ct);
        await Assert.That(correct.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(wrong.TimeEligibility).IsEqualTo(GameplayFactTimeEligibility.NotOpened);
        await Assert.That(correct.Result).IsEqualTo(GameplayFactResult.RightButDue);
        await Assert.That(await f.Db.GameplayFacts.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await f.Db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
    });

    [Test, Timeout(300_000)]
    public Task Timing_updates_rollback_and_other_presentation_updates_preserve_the_schedule(CancellationToken ct) => DockerIntegrationTest.RunAsync(async () =>
    {
        await using var f = await Fixture.CreateAsync(ct);
        var timing = new ChallengeTiming(f.Now.AddMinutes(5), f.Now.AddMinutes(30), f.Now.AddHours(1));
        await using (var transaction = await f.Db.Database.BeginTransactionAsync(ct))
        { await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct); await transaction.RollbackAsync(ct); }
        f.Db.ChangeTracker.Clear(); await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsNull();
        await f.Store.ChangeAsync(new(f.Competition.Id, f.Challenge.Id, timing, f.Now), ct);
        await f.Management.UpdateAsync(new(f.Competition.Id, f.Challenge.Id, 0, false, f.Now, "renamed"), ct);
        await f.Db.Entry(f.Challenge).ReloadAsync(ct);
        await Assert.That(f.Challenge.AutoOpenAt).IsEqualTo(timing.AutoOpenAt);
        await Assert.That(f.Challenge.OpeningState).IsEqualTo(ChallengeOpeningState.Pending);
    });

    private sealed class Fixture(PostgreSqlContainer postgres, NoCtfDbContext db, CtfCompetition competition,
        CompetitionChallenge challenge, DateTimeOffset now) : IAsyncDisposable
    {
        public NoCtfDbContext Db { get; } = db;
        public CtfCompetition Competition { get; } = competition;
        public CompetitionChallenge Challenge { get; } = challenge;
        public DateTimeOffset Now { get; } = now;
        public ChallengeManagementStore Management { get; } = new(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
        public ChallengeTimingStore Store => new(Db, Substitute.For<IPostCommitMessagePublisher>(), Management, NullCompetitionEventRecorder.Instance);
        public static async Task<Fixture> CreateAsync(CancellationToken ct)
        {
            var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            var db = new NoCtfDbContext(options); await db.Database.MigrateAsync(ct);
            var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); var owner = Guid.NewGuid();
            db.Users.Add(new User { Id = owner, UserName = "timing-owner", Email = "timing@test.invalid", PasswordHash = "test",
                AccountStatus = UserAccountStatus.Active, CreatedAt = now, UpdatedAt = now });
            var competition = new CtfCompetition { Id = Guid.NewGuid(), OwnerId = owner, Title = "Timing",
                Status = CompetitionStatus.Running, StartAt = now.AddHours(-1), EndAt = now.AddHours(1),
                ModeConfiguration = TestConfigurations.Competition(GameMode.Ctf), FlagDerivationSecret = new byte[32], CreatedAt = now, UpdatedAt = now };
            db.Add(competition);
            var template = new CtfChallenge { Id = Guid.NewGuid(), OwnerId = owner, Title = "Timing template", Direction = "Web",
                Definition = TestConfigurations.Definition(GameMode.Ctf), CreatedAt = now, UpdatedAt = now };
            db.Add(template); await db.SaveChangesAsync(ct);
            var management = new ChallengeManagementStore(db, Substitute.For<IPostCommitMessagePublisher>(), Substitute.For<IChallengeRuntimeTemplateCatalog>());
            var created = await management.CreateAsync(new(null, competition.Id, template.Id, 0, now), TestConfigurations.Rules(GameMode.Ctf), ct);
            var challenge = await db.CompetitionChallenges.SingleAsync(x => x.Id == created.Challenge!.Id, ct);
            return new(postgres, db, competition, challenge, now);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await postgres.DisposeAsync(); }
    }
}
