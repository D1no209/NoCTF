using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    public async Task RemoveRecordingRawAsync(Guid recordingId,CancellationToken ct)
    {
        if(!await db.LiveSoloRecordings.AsNoTracking().AnyAsync(x=>x.Id==recordingId&&x.RawRemovedAt==null
            && (x.State==LiveSoloRecordingState.Completed&&x.FileId!=null || x.State==LiveSoloRecordingState.Failed),ct))return;
        await files.RemoveAsync(recordingId,ct);
        await TransactionAsync(async()=>{
            var record=await db.LiveSoloRecordings.SingleOrDefaultAsync(x=>x.Id==recordingId
                && (x.State==LiveSoloRecordingState.Completed&&x.FileId!=null || x.State==LiveSoloRecordingState.Failed),ct);
            if(record is not null) { record.RawRemovedAt=clock.GetUtcNow(); record.ReservedBytes=0; }
        },ct);
    }
    public async Task PruneAsync(Guid sessionId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var raw = new HashSet<Guid>();
        var competitionId = await db.LiveSoloMediaSessions.Where(x => x.Id == sessionId).Join(db.LiveSoloMatches, s => s.MatchId, m => m.Id, (s, m) => (Guid?)m.CompetitionId).SingleOrDefaultAsync(ct);
        if (competitionId is Guid id) await TransactionAsync(() => NoCTF.Infrastructure.LiveSolo.Questions.LiveSoloPublicExposure.RememberAsync(db, id, now, ct), ct);
        // References remain until external deletion succeeds, so a lost wakeup can be rebuilt from expired rows.
        foreach (var segment in await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.MediaSessionId == sessionId && x.RemoveAfter <= now).ToArrayAsync(ct))
        {
            var file = await db.Files.AsNoTracking().SingleAsync(x => x.Id == segment.FileId, ct);
            await objects.DeleteObject(file.ObjectKey, ct);
        }
        await TransactionAsync(async () =>
        {
            messages.DiscardPendingMessages(); raw.Clear();
            var expired = await db.LiveSoloProgramSegments.Where(x => x.MediaSessionId == sessionId && x.RemoveAfter <= now).ToArrayAsync(ct);
            foreach (var segment in expired)
            { db.LiveSoloProgramSegments.Remove(segment); db.Files.Remove(await db.Files.SingleAsync(x => x.Id == segment.FileId, ct)); }
            var records = await db.LiveSoloRecordings.Where(x => x.MediaSessionId == sessionId && !x.DisputeHold && x.KeepUntil <= now
                && (x.State == LiveSoloRecordingState.Completed || x.State == LiveSoloRecordingState.Failed || x.State == LiveSoloRecordingState.RequiresReview
                    || x.State == LiveSoloRecordingState.Deleting)).ToArrayAsync(ct);
            foreach (var record in records)
            { raw.Add(record.Id); record.State = LiveSoloRecordingState.Deleting; }
            foreach (var program in await db.LiveSoloProgramCaptures.Where(x => x.MediaSessionId == sessionId && x.ImportedAt != null && x.RawRemovedAt == null
                && (x.State == LiveSoloCaptureState.Completed || x.State == LiveSoloCaptureState.Failed)).Select(x => x.Id).ToArrayAsync(ct)) raw.Add(program);
            foreach (var id in raw) await messages.PublishAsync(new NoCTF.Application.LiveSolo.Media.RemoveLiveSoloCaptureFiles(id));
            foreach(var id in await db.LiveSoloRecordings.Where(x=>x.MediaSessionId==sessionId&&x.RawRemovedAt==null
                && (x.State==LiveSoloRecordingState.Completed&&x.FileId!=null || x.State==LiveSoloRecordingState.Failed)).Select(x=>x.Id).ToArrayAsync(ct))
                await messages.PublishAsync(new NoCTF.Application.LiveSolo.Media.RemoveLiveSoloRecordingRaw(id));
        }, ct);
        await messages.FlushCommittedMessagesAsync();
    }
    public async Task FinishPruneAsync(Guid id, CancellationToken ct)
    {
        var existing = await db.LiveSoloRecordings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (existing is { DisputeHold: true } || existing is not null && existing.State != LiveSoloRecordingState.Deleting) return;
        await files.RemoveAsync(id, ct);
        if (existing?.FileId is Guid existingFileId)
        {
            var file = await db.Files.AsNoTracking().SingleAsync(x => x.Id == existingFileId, ct);
            await objects.DeleteObject(file.ObjectKey, ct);
        }
        await TransactionAsync(async () =>
        {
            var record = await db.LiveSoloRecordings.SingleOrDefaultAsync(x => x.Id == id && x.State == LiveSoloRecordingState.Deleting && !x.DisputeHold, ct);
            if (record is null)
            {
                var program = await db.LiveSoloProgramCaptures.SingleOrDefaultAsync(x => x.Id == id && x.ImportedAt != null, ct);
                if (program is not null) program.RawRemovedAt = clock.GetUtcNow();
                return;
            }
            if (record.FileId is Guid fileId) db.Files.Remove(await db.Files.SingleAsync(x => x.Id == fileId, ct));
            db.LiveSoloRecordings.Remove(record);
        }, ct);
        await messages.FlushCommittedMessagesAsync();
    }
}
