using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Tests.Integration.Persistence;

namespace NoCTF.Tests.Integration.LiveSolo;

[Category("Integration")]
public sealed class LiveSoloStartMediaProofPersistenceTests
{
    [Test, Timeout(300_000)]
    public async Task Persisted_active_job_cannot_start_without_live_matching_export_proof(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var store = f.Store(f.Db);
            var left = await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Left.Id, f.Match.ConcurrencyStamp, f.Now, ct);
            await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Right.Id, left.Match!.ConcurrencyStamp, f.Now, ct);
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct); var program = await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            var job = new LiveSoloExportObservation(program.EgressId!, session.RoomIdentity, LiveSoloExportState.Active, f.Now, null, null, [], program.Id);
            var command = new StartLiveSoloCountdown(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, f.Round.ConcurrencyStamp, f.Now);
            foreach (var invalid in new[] { job with { State = LiveSoloExportState.Starting }, job with { State = LiveSoloExportState.Complete },
                job with { State = LiveSoloExportState.Failed }, job with { RequestId = Guid.NewGuid() }, job with { RoomIdentity = "another-room" },
                job with { StartedAt = null }, job with { StartedAt = f.Now.AddSeconds(1) }, job with { EndedAt = f.Now } })
            {
                f.egress.ListAsync(session.RoomIdentity, ct).Returns([invalid]);
                await Assert.That((await store.StartCountdownAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.MediaUnavailable);
                await Assert.That((await f.Db.LiveSoloRounds.AsNoTracking().SingleAsync(ct)).CountdownAt).IsNull();
            }
            f.egress.ListAsync(session.RoomIdentity, ct).Returns(Task.FromException<IReadOnlyList<LiveSoloExportObservation>>(new HttpRequestException("Unavailable")));
            await Assert.That((await store.StartCountdownAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.MediaUnavailable);
            f.egress.ListAsync(session.RoomIdentity, ct).Returns([job]);
            await Assert.That((await store.StartCountdownAsync(command, ct)).Failure).IsNull();
        });
    }
    [Test, Timeout(300_000)]
    public async Task Generation_change_during_export_check_and_missing_room_prevent_countdown(CancellationToken ct)
    {
        await DockerIntegrationTest.RunAsync(async () =>
        {
            await using var f = await LiveSoloMatchPersistenceTests.Fixture.CreateAsync(ct); await f.PrepareAsync(ct);
            var store = f.Store(f.Db);
            var left = await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Left.Id, f.Match.ConcurrencyStamp, f.Now, ct);
            await store.ConfirmReadyAsync(f.Competition.Id, f.Match.Id, f.Right.Id, left.Match!.ConcurrencyStamp, f.Now, ct);
            var session = await f.Db.LiveSoloMediaSessions.SingleAsync(ct); var program = await f.Db.LiveSoloProgramCaptures.SingleAsync(ct);
            var command = new StartLiveSoloCountdown(f.Competition.Id, f.Match.Id, f.Round.Id, f.Owner.Id, f.Round.ConcurrencyStamp, f.Now);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation([], false));
            await Assert.That((await store.StartCountdownAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.MediaUnavailable);
            f.media.ObserveAsync(session.RoomIdentity, ct).Returns(new LiveSoloRoomObservation(session.Participants.Select(p =>
                new LiveSoloObservedScreen(p.Identity, LiveSoloScreenState.Sharing, "track", f.Now)).ToArray()));
            f.egress.ListAsync(session.RoomIdentity, ct).Returns(async _ =>
            {
                await using var other = new NoCTF.Infrastructure.Persistence.NoCtfDbContext(f.Options);
                (await other.LiveSoloMediaSessions.SingleAsync(ct)).CurrentProgramCaptureId = null; await other.SaveChangesAsync(ct);
                return (IReadOnlyList<LiveSoloExportObservation>)[new(program.EgressId!, session.RoomIdentity, LiveSoloExportState.Active, f.Now, null, null, [], program.Id)];
            });
            await Assert.That((await store.StartCountdownAsync(command, ct)).Failure).IsEqualTo(LiveSoloFailure.MediaUnavailable);
            await Assert.That((await f.Db.LiveSoloRounds.AsNoTracking().SingleAsync(ct)).CountdownAt).IsNull();
        });
    }
}
