using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Storage;
using NoCTF.Infrastructure.LiveSolo.Media;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Infrastructure.Teams.Moderation;
using NoCTF.Tests.Integration.Persistence;
using NoCTF.Persistence.PostgreSql;
using Testcontainers.PostgreSql;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloRecordingPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Generated_recording_decision_migration_upgrades_the_existing_capture_schema(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var postgres = new PostgreSqlBuilder("postgres:17.10-alpine3.24@sha256:742f40ea20b9ff2ff31db5458d127452988a2164df9e17441e191f3b72252193").Build();
            await postgres.StartAsync(ct);
            var options = new DbContextOptionsBuilder<NoCtfDbContext>().UseNpgsql(postgres.GetConnectionString(),
                setup => setup.MigrationsAssembly(typeof(PostgreSqlPersistence).Assembly.FullName)).UseSnakeCaseNamingConvention().Options;
            await using var db = new NoCtfDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261009121727_LiveSoloCaptureRawCleanup", ct);
            await db.Database.MigrateAsync(ct);
            await Assert.That(await db.Database.GetPendingMigrationsAsync(ct)).IsEmpty();
            await Assert.That(await db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(0);
            await Assert.That(await db.LiveSoloRecordings.CountAsync(ct)).IsEqualTo(0);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Publishing_requires_the_entire_event_to_end_and_withdrawal_revokes_public_streams(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var record = await Recording(f, ct);
            var objects = Objects(ct); var store = Store(f.Db, f, objects);
            await Assert.That((await Change(store, f, record, LiveSoloRecordingAction.Publish, ct)).Failure).IsEqualTo(LiveSoloFailure.NotReady);
            await Assert.That(await store.ListAsync(f.Competition.Id, f.Match.Id, Guid.Empty, false, 0, 20, ct)).IsNull();
            await Assert.That(await store.OpenAsync(f.Competition.Id, f.Match.Id, record.Id, f.Left.Id, ct)).IsNull();
            await Assert.That((await store.ListAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, 0, 20, ct))!.Total).IsEqualTo(1);
            (await f.Db.Competitions.SingleAsync(ct)).Status = CompetitionStatus.Finished; await f.Db.SaveChangesAsync(ct);
            await Assert.That((await Change(store, f, record, LiveSoloRecordingAction.Publish, ct)).Failure).IsEqualTo(LiveSoloFailure.NotReady);
            (await f.Db.LiveSoloMatches.SingleAsync(ct)).State = LiveSoloMatchState.Completed; await f.Db.SaveChangesAsync(ct);
            await Assert.That((await Change(store, f, record, LiveSoloRecordingAction.Publish, ct)).Failure).IsNull();
            await Assert.That((await store.ListAsync(f.Competition.Id, f.Match.Id, Guid.Empty, false, 0, 20, ct))!.Total).IsEqualTo(1);
            var stream = await store.OpenAsync(f.Competition.Id, f.Match.Id, record.Id, Guid.Empty, ct);
            await Assert.That(stream).IsNotNull(); await stream!.Content.DisposeAsync();
            await Assert.That(await store.OpenAsync(Guid.NewGuid(), f.Match.Id, record.Id, f.Owner.Id, ct)).IsNull();
            await Change(store, f, record, LiveSoloRecordingAction.Withdraw, ct);
            await Assert.That(await store.OpenAsync(f.Competition.Id, f.Match.Id, record.Id, Guid.Empty, ct)).IsNull();
            await Assert.That(await f.Db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(2);
            var decision = await f.Db.LiveSoloRecordingDecisions.FirstAsync(ct); decision.Reason = "changed";
            await Assert.That(async () => await f.Db.SaveChangesAsync(ct)).Throws<InvalidOperationException>();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Judge_can_hold_but_not_publish_observer_cannot_change_and_deleting_cannot_be_held(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var record = await Recording(f, ct);
            var competition = await f.Db.Competitions.Include(x => x.Collaborators).SingleAsync(ct);
            competition.Collaborators.Add(new() { CompetitionId = competition.Id, UserId = f.Left.Id, Role = CompetitionCollaboratorRole.Judge });
            competition.Collaborators.Add(new() { CompetitionId = competition.Id, UserId = f.Right.Id, Role = CompetitionCollaboratorRole.Observer });
            await f.Db.SaveChangesAsync(ct); var store = Store(f.Db, f, Objects(ct));
            var command = new ChangeLiveSoloRecording(f.Competition.Id, f.Match.Id, record.Id, f.Left.Id, record.ConcurrencyStamp, LiveSoloRecordingAction.Publish, "review");
            await Assert.That((await store.ChangeAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That((await store.ChangeAsync(command with { ActorId = f.Right.Id, Action = LiveSoloRecordingAction.Hold }, ct)).Failure).IsEqualTo(LiveSoloFailure.Forbidden);
            await Assert.That((await store.ChangeAsync(command with { Action = LiveSoloRecordingAction.Hold }, ct)).Failure).IsNull();
            f.Now = record.KeepUntil.AddSeconds(1);
            await Assert.That(await store.MayOpenAsync(f.Competition.Id, f.Match.Id, record.Id, f.Right.Id, ct)).IsTrue();
            await Change(store, f, record, LiveSoloRecordingAction.ReleaseHold, ct);
            await Assert.That(await store.MayOpenAsync(f.Competition.Id, f.Match.Id, record.Id, f.Right.Id, ct)).IsFalse();
            record.State = LiveSoloRecordingState.Deleting; await f.Db.SaveChangesAsync(ct);
            await Assert.That((await Change(store, f, record, LiveSoloRecordingAction.Hold, ct)).Failure).IsEqualTo(LiveSoloFailure.Conflict);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Concurrent_decisions_consume_one_revision_and_record_one_audit(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var record = await Recording(f, ct);
            var command = new ChangeLiveSoloRecording(f.Competition.Id, f.Match.Id, record.Id, f.Owner.Id, record.ConcurrencyStamp, LiveSoloRecordingAction.Hold, "dispute");
            async Task<LiveSoloRecordingChangeResult> Apply()
            { await using var db = new NoCtfDbContext(f.Options); return await Store(db, f, Objects(ct)).ChangeAsync(command, ct); }
            var results = await Task.WhenAll(Apply(), Apply());
            await Assert.That(results.Count(x => x.Failure is null)).IsEqualTo(1);
            await Assert.That(results.Count(x => x.Failure == LiveSoloFailure.Conflict)).IsEqualTo(1);
            await Assert.That(await f.Db.LiveSoloRecordingDecisions.CountAsync(ct)).IsEqualTo(1);
        });
    }
    [Test, Timeout(300_000)]
    public async Task Authorization_is_rechecked_after_opening_a_stream_and_disabled_mode_keeps_staff_history(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); var record = await Recording(f, ct);
            (await f.Db.Competitions.SingleAsync(ct)).Status = CompetitionStatus.Finished;
            (await f.Db.LiveSoloMatches.SingleAsync(ct)).State = LiveSoloMatchState.Completed; record.Published = true; await f.Db.SaveChangesAsync(ct);
            var stream = new MemoryStream([1, 2, 3]); var objects = Objects(ct);
            async Task<Stream?> Withdraw()
            {
                await using var other = new NoCtfDbContext(f.Options);
                (await other.LiveSoloRecordings.SingleAsync(ct)).Published = false; await other.SaveChangesAsync(ct); return stream;
            }
            objects.OpenRead("recording-test", ct).Returns(_ => Withdraw());
            var store = Store(f.Db, f, objects);
            await Assert.That(await store.OpenAsync(f.Competition.Id, f.Match.Id, record.Id, Guid.Empty, ct)).IsNull();
            await Assert.That(stream.CanRead).IsFalse();
            (await f.Db.Set<LiveSoloCompetitionModeConfiguration>().SingleAsync(ct)).Enabled = false; await f.Db.SaveChangesAsync(ct);
            await Assert.That(await store.ListAsync(f.Competition.Id, f.Match.Id, f.Owner.Id, true, 0, 20, ct)).IsNotNull();
            await Assert.That(await store.ListAsync(f.Competition.Id, f.Match.Id, Guid.Empty, false, 0, 20, ct)).IsNull();
        });
    }
    private static LiveSoloRecordingStore Store(NoCtfDbContext db, LiveSoloMatchPersistenceTests.Fixture f, IStore objects)
        => new(db, new CompetitionModerationAuthorizer(db), objects, f.Clock);
    private static Task<LiveSoloRecordingChangeResult> Change(LiveSoloRecordingStore store, LiveSoloMatchPersistenceTests.Fixture f, LiveSoloRecording record, LiveSoloRecordingAction action, CancellationToken ct)
        => store.ChangeAsync(new(f.Competition.Id, f.Match.Id, record.Id, f.Owner.Id, record.ConcurrencyStamp, action, "reason"), ct);
    private static IStore Objects(CancellationToken ct)
    {
        var objects = Substitute.For<IStore>(); objects.ObjectExists(Arg.Any<string>(), ct).Returns(true);
        objects.OpenRead(Arg.Any<string>(), ct).Returns(_ => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3, 4]))); return objects;
    }
    private static async Task<LiveSoloRecording> Recording(LiveSoloMatchPersistenceTests.Fixture f, CancellationToken ct)
    {
        await f.PrepareAsync(ct); var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct);
        var file = new StoredFile { Id = Guid.NewGuid(), ObjectKey = "recording-test", FileName = "recording.mp4", ContentType = "video/mp4",
            ByteLength = 4, Sha256 = new byte[32], CreatedAt = f.Now };
        f.Db.Files.Add(file);
        var record = new LiveSoloRecording { Id = Guid.NewGuid(), MediaSessionId = session.Id, UserId = f.Left.Id, VideoTrackId = "screen-test",
            State = LiveSoloRecordingState.Completed, FileId = file.Id, CreatedAt = f.Now, StartedAt = f.Now, EndedAt = f.Now.AddSeconds(5), KeepUntil = f.Now.AddDays(30) };
        f.Db.LiveSoloRecordings.Add(record); await f.Db.SaveChangesAsync(ct); return record;
    }
}
