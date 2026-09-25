using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Competitions.Administration;
using NoCTF.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.Persistence;

[Category("Integration")]
public sealed class CompetitionForceDeleteBoundaryTests
{
    [Test, Timeout(300_000)]
    public Task Complete_awdp_chain_deletes_only_owned_data(CancellationToken ct) => RunAsync(async (db, fixture) =>
    {
        // Prove the former first DELETE is rejected by the real production-shaped FK chain.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var failure = await Assert.That(async () => await db.RuntimeInstances
                .Where(x => x.CompetitionId == fixture.Id).ExecuteDeleteAsync(ct)).Throws<PostgresException>();
            await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
            await transaction.RollbackAsync(ct);
        }
        var store = new AdminCompetitionStore(db);
        var preview = await store.PreviewHardDeleteAsync(fixture.Id, fixture.OwnerId, true, ct);
        await Assert.That(preview!.References.Single(x => x.Kind == CompetitionHardDeleteReferenceKind.Notification).Count).IsEqualTo(2);
        var result = await store.ForceDeleteAsync(fixture.Command, true, ct);
        await Assert.That(result.State).IsEqualTo(CompetitionForceDeleteState.Deleted);
        await Assert.That(await db.PatchUploads.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.RuntimeInstances.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(0);
        await Assert.That(await db.CompetitionChallenges.CountAsync(ct)).IsEqualTo(1);
        await Assert.That((await db.Competitions.SingleAsync(ct)).Id).IsEqualTo(fixture.OtherId);
        await Assert.That(await db.Challenges.CountAsync(ct)).IsEqualTo(1);
        await Assert.That(await db.ChallengeFlags.CountAsync(ct)).IsEqualTo(1);
        await Assert.That(await db.Set<NoCTF.Domain.Challenges.ChallengeAttachment>().CountAsync(ct)).IsEqualTo(1);
        await Assert.That(await db.Users.CountAsync(ct)).IsEqualTo(1);
        // File cleanup is queued separately; this transaction preserves every immutable file record.
        await Assert.That(await db.Files.Select(file => file.Id).ToArrayAsync(ct)).IsEquivalentTo(fixture.CleanupFileIds);
        await Assert.That(await db.Notifications.CountAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct)).IsEqualTo(1);
        await Assert.That(await db.Notifications.AnyAsync(x => x.Id == fixture.MemberId, ct)).IsFalse();
    }, ct);

    [Test, Timeout(300_000)]
    [Arguments(0)] [Arguments(1)] [Arguments(2)] [Arguments(3)] [Arguments(4)]
    public Task Cross_scope_notifications_block_without_deleting_other_threads(int scenario, CancellationToken ct) => RunAsync(async (db, fixture) =>
    {
        var conflict = fixture.Notification(
            Guid.NewGuid(),
            scenario is 0 or 1 ? fixture.OtherId : null,
            scenario == 4 ? NotificationKind.CompetitionForceDeleted : NotificationKind.Message);
        if (scenario is 0 or 2 or 4) conflict.ReplyToId = fixture.RootId;
        if (scenario == 1) conflict.ThreadRootId = fixture.RootId;
        db.Notifications.Add(conflict);
        await db.SaveChangesAsync(ct);
        if (scenario == 3)
        {
            var member = fixture.Notification(Guid.NewGuid(), null);
            member.ThreadRootId = fixture.RootId;
            member.ReplyToId = conflict.Id;
            db.Notifications.Add(member);
            await db.SaveChangesAsync(ct);
            conflict = member;
        }
        db.ChangeTracker.Clear();
        var store = new AdminCompetitionStore(db);
        var result = await store.ForceDeleteAsync(fixture.Command, true, ct);
        await Assert.That(result.State).IsEqualTo(CompetitionForceDeleteState.NotificationScopeConflict);
        await Assert.That(result.ConflictingNotificationIds).Contains(conflict.Id);
        await Assert.That(result.Preview!.CanForceDelete).IsFalse();
        await AssertIntactAsync(db, fixture, ct);
        await Assert.That(await db.Notifications.AnyAsync(x => x.Id == conflict.Id, ct)).IsTrue();
    }, ct);

    [Test, Timeout(300_000)]
    public Task Guards_reject_active_competitions_resources_invalid_confirmation_and_non_admin(CancellationToken ct) => RunAsync(async (db, fixture) =>
    {
        var store = new AdminCompetitionStore(db);
        var useCase = new ForceDeleteCompetition(store);
        await Assert.That((await useCase.ExecuteAsync(fixture.Command, false, ct)).State).IsEqualTo(CompetitionForceDeleteState.NotFound);
        await Assert.That((await useCase.ExecuteAsync(fixture.Command with { ConfirmationTitle = "wrong" }, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.ConfirmationMismatch);
        await Assert.That((await useCase.ExecuteAsync(fixture.Command with { Reason = " " }, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.InvalidReason);
        foreach (var status in new[] { CompetitionStatus.Running, CompetitionStatus.Paused })
        {
            await db.Competitions.Where(x => x.Id == fixture.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status), ct);
            await Assert.That((await useCase.ExecuteAsync(fixture.Command, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.ActiveCompetition);
        }
        await db.Competitions.Where(x => x.Id == fixture.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, CompetitionStatus.Finished), ct);
        foreach (var state in new[] { RuntimeState.Queued, RuntimeState.Provisioning, RuntimeState.Running, RuntimeState.Stopping })
        {
            await db.RuntimeInstances.Where(x => x.Id == fixture.RuntimeIds[0]).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, state), ct);
            await Assert.That((await useCase.ExecuteAsync(fixture.Command, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.ActiveRuntimeResource);
        }
        await db.RuntimeInstances.Where(x => x.Id == fixture.RuntimeIds[0]).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.State, RuntimeState.Failed)
                .SetProperty(x => x.FailureCode, RuntimeFailureCode.CleanupFailed)
                .SetProperty(x => x.RunnerId, "still-assigned"), ct);
        await Assert.That((await useCase.ExecuteAsync(fixture.Command, true, ct)).State).IsEqualTo(CompetitionForceDeleteState.ActiveRuntimeResource);
        await AssertIntactAsync(db, fixture, ct);
        await Assert.That(await db.Notifications.CountAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct)).IsEqualTo(0);
    }, ct);

    [Test, Timeout(300_000)]
    public Task Failure_during_delete_rolls_back_every_scoped_record(CancellationToken ct) => RunAsync(async (db, fixture) =>
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_test_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected deletion failure'; END $$;
            CREATE TRIGGER reject_delete BEFORE DELETE ON competitions
            FOR EACH ROW EXECUTE FUNCTION reject_test_delete();
            """, ct);
        await Assert.That(async () => await new AdminCompetitionStore(db).ForceDeleteAsync(fixture.Command, true, ct))
            .Throws<PostgresException>();
        db.ChangeTracker.Clear();
        await AssertIntactAsync(db, fixture, ct);
        await Assert.That(await db.Notifications.CountAsync(x => x.Kind == NotificationKind.CompetitionForceDeleted, ct)).IsEqualTo(0);
    }, ct);

    [Test, Timeout(300_000)]
    public Task An_inconsistent_team_reference_is_rejected_before_delete(CancellationToken ct) => RunAsync(async (db, fixture) =>
    {
        var team = await db.Teams.SingleAsync(ct);
        var templateFlag = await db.ChallengeFlags.SingleAsync(ct);
        templateFlag.TeamId = team.Id;
        await Assert.That(async () => await db.SaveChangesAsync(ct))
            .Throws<InvalidOperationException>();
        db.ChangeTracker.Clear();
        await AssertIntactAsync(db, fixture, ct);
        await Assert.That(await db.ChallengeFlags.AnyAsync(x => x.ChallengeId == fixture.TemplateId, ct)).IsTrue();
    }, ct);

    private static async Task AssertIntactAsync(NoCtfDbContext db, CompetitionForceDeleteFixture fixture, CancellationToken ct)
    {
        await Assert.That(await db.Competitions.CountAsync(ct)).IsEqualTo(2);
        await Assert.That(await db.PatchUploads.CountAsync(ct)).IsEqualTo(3);
        await Assert.That(await db.RuntimeInstances.CountAsync(ct)).IsEqualTo(3);
        await Assert.That(await db.GameplayFacts.CountAsync(ct)).IsEqualTo(3);
        await Assert.That(await db.Notifications.AnyAsync(x => x.Id == fixture.RootId, ct)).IsTrue();
        await Assert.That(await db.Notifications.AnyAsync(x => x.Id == fixture.MemberId, ct)).IsTrue();
    }

    private static Task RunAsync(Func<NoCtfDbContext, CompetitionForceDeleteFixture, Task> action, CancellationToken ct) =>
        DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.Database.EnsureCreatedAsync(ct);
            var fixture = new CompetitionForceDeleteFixture();
            await fixture.SeedAsync(db, ct);
            await action(db, fixture);
        });
}
