using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class ParticipationLockOrderTests
{
    [Test, Timeout(300_000)]
    public async Task Shared_competition_admission_allows_parallel_teams_and_same_team_FK_progress(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () => {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build(); await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture(); Guid teamId; Guid challengeId;
            await using (var setup = new NoCtfDbContext(options)) {
                await setup.Database.EnsureCreatedAsync(ct); await fixture.SeedAsync(setup, ct);
                teamId = await setup.Teams.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
                challengeId = await setup.CompetitionChallenges.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
            }
            await using var submit = new NoCtfDbContext(options); await using var runtime = new NoCtfDbContext(options);
            await using var st = await submit.Database.BeginTransactionAsync(ct); await using var rt = await runtime.Database.BeginTransactionAsync(ct);
            var quota = new NoCTF.Infrastructure.Runtime.Instances.TeamRuntimeQuota(new AsyncKeyedLock.AsyncKeyedLocker<string>());
            var attempts = new NoCTF.Infrastructure.GameplayFacts.Intake.GameplayFactAttemptCriticalSection(new AsyncKeyedLock.AsyncKeyedLocker<string>());
            using var submitting = await attempts.AcquireAsync(submit, teamId, challengeId, NoCTF.Domain.Gameplay.GameplayFactKind.FlagAttempt, ct);
            // Runtime admission can acquire the same competition read lock while submission holds its team.
            await NoCTF.Infrastructure.Competitions.Participation.CompetitionParticipationLock.AcquireAsync(runtime, fixture.Id, ct);
            var waiting = quota.AcquireLockAsync(runtime, fixture.Id, teamId, ct).AsTask();
            await submit.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO competition_events (id,competition_id,kind,level,visibility,subject_type,subject_id,occurred_at,payload_json) VALUES ({Guid.NewGuid()},{fixture.Id},0,0,0,1,{fixture.Id},now(),jsonb_build_object('schemaVersion',1))", ct);
            await st.CommitAsync(ct);
            using var runtimeLease = await waiting;
            await rt.CommitAsync(ct);
            // No global exclusive competition lock: two other scopes acquire admission together.
            await using var a = new NoCtfDbContext(options); await using var b = new NoCtfDbContext(options);
            await using var at = await a.Database.BeginTransactionAsync(ct); await using var bt = await b.Database.BeginTransactionAsync(ct);
            await Task.WhenAll(NoCTF.Infrastructure.Competitions.Participation.CompetitionParticipationLock.AcquireAsync(a, fixture.Id, ct),
                NoCTF.Infrastructure.Competitions.Participation.CompetitionParticipationLock.AcquireAsync(b, fixture.Id, ct));
            await at.CommitAsync(ct); await bt.CommitAsync(ct);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Legacy_competition_update_lock_and_team_first_insert_form_a_real_foreign_key_deadlock(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;
            var fixture = new CompetitionForceDeleteFixture();
            await using (var db = new NoCtfDbContext(options)) { await db.Database.EnsureCreatedAsync(ct); await fixture.SeedAsync(db, ct); }
            await using var submit = new NoCtfDbContext(options); await using var runtime = new NoCtfDbContext(options);
            var teamId = await submit.Teams.Where(x => x.CompetitionId == fixture.Id).Select(x => x.Id).SingleAsync(ct);
            await using var st = await submit.Database.BeginTransactionAsync(ct);
            await using var rt = await runtime.Database.BeginTransactionAsync(ct);
            await submit.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM teams WHERE id={teamId} FOR UPDATE", ct);
            await runtime.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM competitions WHERE id={fixture.Id} FOR UPDATE", ct);
            var waitingForTeam = Observe(async () => await runtime.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM teams WHERE id={teamId} FOR UPDATE", ct));
            var insert = Observe(async () => await submit.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO competition_events (id,competition_id,kind,level,visibility,subject_type,subject_id,occurred_at,payload_json) VALUES ({Guid.NewGuid()},{fixture.Id},0,0,0,{(short)NoCTF.Domain.Shared.EntityReferenceKind.Competition},{fixture.Id},now(),jsonb_build_object('schemaVersion',1))", ct));
            var codes = await Task.WhenAll(waitingForTeam, insert);
            await st.RollbackAsync(ct); await rt.RollbackAsync(ct);
            await Assert.That(codes).Contains(PostgresErrorCodes.DeadlockDetected);
        });
    }
    private static async Task<string?> Observe(Func<Task> action)
    {
        try { await action(); return null; } catch (PostgresException exception) { Console.WriteLine($"Reproduction SQLSTATE {exception.SqlState}: {exception.MessageText}"); return exception.SqlState; }
    }
}
