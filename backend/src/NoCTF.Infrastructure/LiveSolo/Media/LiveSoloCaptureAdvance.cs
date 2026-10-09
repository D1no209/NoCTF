using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    public async Task AdvanceAsync(Guid sessionId, CancellationToken ct)
    {
        await EnsureAsync(sessionId, ct);
        var session = await db.LiveSoloMediaSessions.AsNoTracking().Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is null) return;
        var competitionId = await db.LiveSoloMatches.Where(x => x.Id == session.MatchId).Select(x => x.CompetitionId).SingleAsync(ct);
        await TransactionAsync(() => NoCTF.Infrastructure.LiveSolo.Questions.LiveSoloPublicExposure.RememberAsync(db, competitionId, clock.GetUtcNow(), ct), ct);
        if (session.State != LiveSoloMediaState.Ready && await db.LiveSoloProgramCaptures.AnyAsync(x => x.MediaSessionId == session.Id
            && (x.State == LiveSoloCaptureState.Active || x.State == LiveSoloCaptureState.Stopping), ct))
            await TransactionAsync(() => FrameAsync(session, ct), ct);
        var active = session.State == LiveSoloMediaState.Ready && await db.LiveSoloMatches.AnyAsync(x => x.Id == session.MatchId && x.CurrentMediaSessionId == session.Id, ct);
        var jobs = await egress.ListAsync(session.RoomIdentity, ct);
        foreach (var program in await db.LiveSoloProgramCaptures.Where(x => x.MediaSessionId == sessionId && x.State != LiveSoloCaptureState.Failed
            && (x.State != LiveSoloCaptureState.RequiresReview || x.EgressId == null) && (x.State != LiveSoloCaptureState.Completed || x.ImportedAt == null)).ToArrayAsync(ct))
        {
            var current = jobs.SingleOrDefault(x => x.Id == program.EgressId || x.RequestId == program.Id);
            if (program.State == LiveSoloCaptureState.Pending && active && session.Participants.Any(x => x.ScreenState == LiveSoloScreenState.Sharing))
            {
                var claimed = false;
                await TransactionAsync(async () =>
                {
                    claimed = false;
                    await db.Entry(program).ReloadAsync(ct);
                    if (program.State != LiveSoloCaptureState.Pending) return;
                    if (!await db.LiveSoloMediaSessions.AnyAsync(x => x.Id == session.Id && x.State == LiveSoloMediaState.Ready
                        && x.CurrentProgramCaptureId == program.Id, ct)) return;
                    program.State = LiveSoloCaptureState.Starting; program.RequestedAt = clock.GetUtcNow();
                    claimed = true;
                }, ct);
                if (!claimed) continue;
                try { current = await egress.StartAsync(new(program.Id, session.RoomIdentity, LiveSoloExportKind.Program), ct); }
                catch (HttpRequestException) { program.State = LiveSoloCaptureState.RequiresReview; await db.SaveChangesAsync(ct); continue; }
            }
            if (current is null)
            {
                if (program.State == LiveSoloCaptureState.Starting && program.RequestedAt < clock.GetUtcNow().AddMinutes(-1))
                { program.State = LiveSoloCaptureState.RequiresReview; await db.SaveChangesAsync(ct); }
                else if (program.State == LiveSoloCaptureState.Pending && !active) { program.State = LiveSoloCaptureState.Failed; await db.SaveChangesAsync(ct); }
                continue;
            }
            program.EgressId = current.Id; program.StartedAt ??= current.StartedAt;
            program.EndedAt = current.EndedAt; program.State = ProgramState(current.State);
            await db.SaveChangesAsync(ct);
            if (current.State is LiveSoloExportState.Active or LiveSoloExportState.Complete)
            {
                if (await ImportSegmentsAsync(session, program, ct) && current.State == LiveSoloExportState.Complete)
                { program.ImportedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); }
            }
            if (!active && current.State is LiveSoloExportState.Starting or LiveSoloExportState.Active)
            { await egress.StopAsync(current.Id, ct); program.State = LiveSoloCaptureState.Stopping; await db.SaveChangesAsync(ct); }
        }
        foreach (var record in await db.LiveSoloRecordings.Where(x => x.MediaSessionId == sessionId && x.State != LiveSoloRecordingState.Completed
            && x.State != LiveSoloRecordingState.Failed && (x.State != LiveSoloRecordingState.RequiresReview || x.EgressId == null && x.RequestedAt != null) && x.State != LiveSoloRecordingState.Deleting).ToArrayAsync(ct))
        {
            var current = jobs.SingleOrDefault(x => x.Id == record.EgressId || x.RequestId == record.Id);
            var stillSharing = active && session.Participants.Any(x => x.UserId == record.UserId && x.ScreenTrackId == record.VideoTrackId && x.ScreenState == LiveSoloScreenState.Sharing);
            if (record.State == LiveSoloRecordingState.Pending && stillSharing)
            {
                var claimed = false;
                await TransactionAsync(async () =>
                {
                    claimed = false;
                    await db.Entry(record).ReloadAsync(ct);
                    if (record.State != LiveSoloRecordingState.Pending) return;
                    if (!await db.LiveSoloMediaSessions.AnyAsync(x => x.Id == session.Id && x.State == LiveSoloMediaState.Ready
                        && db.LiveSoloMatches.Any(m=>m.Id==x.MatchId&&m.CurrentMediaSessionId==x.Id), ct)
                        || !await db.Set<LiveSoloMediaParticipant>().AnyAsync(p=>p.MediaSessionId==session.Id&&p.UserId==record.UserId
                            &&p.ScreenTrackId==record.VideoTrackId&&p.ScreenState==LiveSoloScreenState.Sharing,ct)) return;
                    var used=await RecordingCapacityUsedAsync(ct);var reservation=checked(options.RecordingExportLimitBytes*2);
                    if (reservation>options.RecordingQuotaBytes-used) {record.State=LiveSoloRecordingState.RequiresReview;record.Failure=LiveSoloRecordingFailure.CapacityUnavailable;return;}
                    record.ReservedBytes=reservation;
                    record.State = LiveSoloRecordingState.Starting; record.RequestedAt = clock.GetUtcNow();
                    record.Failure=null;
                    claimed = true;
                }, ct);
                if (!claimed) continue;
                try { current = await egress.StartAsync(new(record.Id, session.RoomIdentity, LiveSoloExportKind.ScreenRecording, record.VideoTrackId), ct); }
                catch (HttpRequestException) { record.State = LiveSoloRecordingState.RequiresReview; record.Failure=LiveSoloRecordingFailure.StartUncertain;await db.SaveChangesAsync(ct); continue; }
            }
            if (current is null)
            {
                if (record.State == LiveSoloRecordingState.Starting && record.RequestedAt < clock.GetUtcNow().AddMinutes(-1))
                { record.State = LiveSoloRecordingState.RequiresReview;record.Failure=LiveSoloRecordingFailure.StartUncertain; await db.SaveChangesAsync(ct); }
                else if (record.State == LiveSoloRecordingState.Pending && !stillSharing) { record.State = LiveSoloRecordingState.Failed; await db.SaveChangesAsync(ct); }
                continue;
            }
            record.EgressId = current.Id; record.StartedAt ??= current.StartedAt; record.EndedAt = current.EndedAt;
            record.State = RecordingState(current.State); await db.SaveChangesAsync(ct);
            if (current.State == LiveSoloExportState.Complete) await ImportRecordingAsync(session, record, current, ct);
            if (current.State is LiveSoloExportState.Failed or LiveSoloExportState.Aborted or LiveSoloExportState.LimitReached)
            {
                record.Failure=LiveSoloRecordingFailure.ExportFailed;await db.SaveChangesAsync(ct);
                await RemoveRecordingRawAsync(record.Id, ct);
            }
            var limitReached=current.State==LiveSoloExportState.Active && await files.RecordingLengthAsync(record.Id,ct)
                >= options.RecordingExportLimitBytes-4L*1024*1024;
            if ((!stillSharing || limitReached) && current.State is LiveSoloExportState.Starting or LiveSoloExportState.Active)
            { await egress.StopAsync(current.Id, ct); record.State = LiveSoloRecordingState.Finalizing; await db.SaveChangesAsync(ct); }
        }
    }
    private async Task<long> RecordingCapacityUsedAsync(CancellationToken ct)
    {
        var retained=await db.LiveSoloRecordings.Where(x=>x.FileId!=null).Join(db.Files,r=>r.FileId,f=>(Guid?)f.Id,(r,f)=>f.ByteLength*(r.RawRemovedAt==null?2:1)).SumAsync(ct);
        var reserved=await db.LiveSoloRecordings.Where(x=>x.FileId==null).SumAsync(x=>x.ReservedBytes,ct);
        return checked(retained+reserved);
    }
    private static LiveSoloCaptureState ProgramState(LiveSoloExportState state) => state switch
    {
        LiveSoloExportState.Starting => LiveSoloCaptureState.Starting, LiveSoloExportState.Active => LiveSoloCaptureState.Active,
        LiveSoloExportState.Ending => LiveSoloCaptureState.Stopping, LiveSoloExportState.Complete => LiveSoloCaptureState.Completed,
        _ => LiveSoloCaptureState.Failed,
    };
    private static LiveSoloRecordingState RecordingState(LiveSoloExportState state) => state switch
    {
        LiveSoloExportState.Starting => LiveSoloRecordingState.Starting, LiveSoloExportState.Active => LiveSoloRecordingState.Recording,
        LiveSoloExportState.Ending => LiveSoloRecordingState.Finalizing, LiveSoloExportState.Complete => LiveSoloRecordingState.Finalizing,
        _ => LiveSoloRecordingState.Failed,
    };
}
