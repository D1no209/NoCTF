using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    private async Task<bool> ImportSegmentsAsync(LiveSoloMediaSession session, LiveSoloProgramCapture program, CancellationToken ct)
    {
        if (program.StartedAt is not { } started) return false;
        var segments = await files.SegmentsAsync(program.Id, ct);
        if (segments.Count == 0) return false;
        var cursor = program.ImportedThrough ?? started;
        if(program.ImportedThrough is null && await db.LiveSoloProgramSegments.AnyAsync(x=>x.ProgramCaptureId==program.Id,ct))
            throw new InvalidDataException("A media capture without a durable import cursor cannot continue.");
        foreach (var item in segments.OrderBy(x => x.Sequence))
        {
            if(item.Sequence<program.NextSegmentSequence)
            {await files.RemoveSegmentAsync(program.Id,item.FileName,ct);continue;}
            if(item.Sequence!=program.NextSegmentSequence)throw new InvalidDataException("Media export has a missing segment.");
            cursor=program.ImportedThrough??started;
            var from = cursor; cursor += item.Duration;
            var frame = await db.LiveSoloProgramFrames.AsNoTracking().Where(x => x.MediaSessionId == session.Id && x.Kind==LiveSoloProgramFrameKind.VideoObservation && x.OccurredAt <= cursor)
                .OrderByDescending(x => x.OccurredAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (frame is null) return false;
            await using var content = await files.OpenAsync(program.Id, item.FileName, ct);
            if (content is null) return false;
            var id = Guid.CreateVersion7(clock.GetUtcNow());
            var upload = await uploads.CreateAsync(id, $"live-solo/program/{session.Id:N}/{id:N}", item.FileName, "video/mp2t", content, clock.GetUtcNow(), ct);
            var attached = false;
            try
            {
                await TransactionAsync(async () =>
                {
                    attached = false;
                    await db.Entry(program).ReloadAsync(ct);
                    if(program.NextSegmentSequence>item.Sequence)return;
                    if(program.NextSegmentSequence!=item.Sequence || (program.ImportedThrough??started)!=from)
                        throw new InvalidDataException("Media import cursor changed.");
                    // Upload/observation time is a conservative floor. Provider timing never permits early publication.
                    var observed = clock.GetUtcNow(); var publicAt = LiveSoloProgramPolicy.PublicationTime(cursor, observed, session.PublicDelaySeconds);
                    db.LiveSoloProgramSegments.Add(new() { Id = Guid.CreateVersion7(observed), MediaSessionId = session.Id,
                        ProgramCaptureId = program.Id, FrameId = frame.Value, Sequence = item.Sequence, FileId = upload.FileId,
                        StartedAt = from, EndedAt = cursor, PublicAt = publicAt, RemoveAfter = publicAt.AddMinutes(5) });
                    program.NextSegmentSequence=checked(item.Sequence+1);program.ImportedThrough=cursor;
                    var recovered=program.StalledAt!=null;
                    program.LastFragmentImportedAt=observed;program.StalledAt=null;
                    if(recovered)
                    {
                        var match=await db.LiveSoloMatches.SingleAsync(x=>x.Id==session.MatchId,ct);
                        await messages.PublishAsync(new NoCTF.Application.LiveSolo.Realtime.LiveSoloMatchChanged(match.CompetitionId,match.Id,observed));
                    }
                    attached = true;
                }, ct);
            }
            finally { if (!attached) await uploads.AbandonAsync(upload.FileId); }
            await content.DisposeAsync();
            await files.RemoveSegmentAsync(program.Id,item.FileName,ct);
        }
        return true;
    }
    private async Task ImportRecordingAsync(LiveSoloMediaSession session, LiveSoloRecording record, LiveSoloExportObservation observation, CancellationToken ct)
    {
        if (record.FileId is not null) { record.State = LiveSoloRecordingState.Completed; await db.SaveChangesAsync(ct); return; }
        var file = observation.Files.SingleOrDefault(x => x.ObjectKey.EndsWith("/recording.mp4", StringComparison.Ordinal));
        if (file is null || file.ByteLength <= 0) return;
        if(file.ByteLength>options.RecordingExportLimitBytes) {await RecordingFailureAsync(session,record,LiveSoloRecordingState.RequiresReview,LiveSoloRecordingFailure.ExportTooLarge,ct);return;}
        await using var content = await files.OpenAsync(record.Id, "recording.mp4", ct);
        if (content is null) return;
        var id = Guid.CreateVersion7(clock.GetUtcNow());
        var upload = await uploads.CreateAsync(id, $"live-solo/recordings/{session.Id:N}/{id:N}", "recording.mp4", "video/mp4", content, clock.GetUtcNow(), ct);
        var attached = false;
        try
        {
            await TransactionAsync(async () =>
            {
                attached = false; await db.Entry(record).ReloadAsync(ct);
                if (record.FileId is not null) return;
                var used=await RecordingCapacityUsedAsync(ct);
                if (upload.ByteLength>options.RecordingExportLimitBytes || checked(upload.ByteLength*2)>options.RecordingQuotaBytes-used+record.ReservedBytes)
                { record.State = LiveSoloRecordingState.RequiresReview;record.Failure=upload.ByteLength>options.RecordingExportLimitBytes
                    ? LiveSoloRecordingFailure.ExportTooLarge : LiveSoloRecordingFailure.ArchiveCapacityUnavailable;
                    await CaptureAlertAsync(session,record.Id,record.ConcurrencyStamp,LiveSoloMediaAlertKind.RecordingFailed,ct);return; }
                record.FileId = upload.FileId; record.State = LiveSoloRecordingState.Completed;
                record.ReservedBytes=0;
                record.Failure=null;
                record.KeepUntil = (record.EndedAt ?? clock.GetUtcNow()).AddDays(session.RecordingRetentionDays); attached = true;
                await messages.PublishAsync(new NoCTF.Application.LiveSolo.Media.RemoveLiveSoloRecordingRaw(record.Id));
            }, ct);
        }
        finally { if (!attached) await uploads.AbandonAsync(upload.FileId); }
        if(attached)await messages.FlushCommittedMessagesAsync();
    }
}
