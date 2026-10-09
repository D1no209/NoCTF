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
        var cursor = started;
        foreach (var item in segments.OrderBy(x => x.Sequence))
        {
            var from = cursor; cursor += item.Duration;
            if (await db.LiveSoloProgramSegments.AnyAsync(x => x.ProgramCaptureId == program.Id && x.Sequence == item.Sequence, ct)) continue;
            var frame = await db.LiveSoloProgramFrames.AsNoTracking().Where(x => x.MediaSessionId == session.Id && x.OccurredAt <= cursor)
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
                    if (await db.LiveSoloProgramSegments.AnyAsync(x => x.ProgramCaptureId == program.Id && x.Sequence == item.Sequence, ct)) return;
                    // Upload/observation time is a conservative floor. Provider timing never permits early publication.
                    var observed = clock.GetUtcNow(); var publicAt = LiveSoloProgramPolicy.PublicationTime(cursor, observed, session.PublicDelaySeconds);
                    db.LiveSoloProgramSegments.Add(new() { Id = Guid.CreateVersion7(observed), MediaSessionId = session.Id,
                        ProgramCaptureId = program.Id, FrameId = frame.Value, Sequence = item.Sequence, FileId = upload.FileId,
                        StartedAt = from, EndedAt = cursor, PublicAt = publicAt, RemoveAfter = publicAt.AddMinutes(5) });
                    attached = true;
                }, ct);
            }
            finally { if (!attached) await uploads.AbandonAsync(upload.FileId); }
        }
        return true;
    }
    private async Task ImportRecordingAsync(LiveSoloMediaSession session, LiveSoloRecording record, LiveSoloExportObservation observation, CancellationToken ct)
    {
        if (record.FileId is not null) { record.State = LiveSoloRecordingState.Completed; await db.SaveChangesAsync(ct); return; }
        var file = observation.Files.SingleOrDefault(x => x.ObjectKey.EndsWith("/recording.mp4", StringComparison.Ordinal));
        if (file is null || file.ByteLength <= 0) return;
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
                var retained = await db.LiveSoloRecordings.Where(x => x.FileId != null)
                    .Join(db.Files, r => r.FileId, f => (Guid?)f.Id, (_, f) => f.ByteLength).SumAsync(ct);
                if (upload.ByteLength > options.RecordingQuotaBytes - retained)
                { record.State = LiveSoloRecordingState.RequiresReview; return; }
                record.FileId = upload.FileId; record.State = LiveSoloRecordingState.Completed;
                record.KeepUntil = (record.EndedAt ?? clock.GetUtcNow()).AddDays(session.RecordingRetentionDays); attached = true;
            }, ct);
        }
        finally { if (!attached) await uploads.AbandonAsync(upload.FileId); }
    }
}
