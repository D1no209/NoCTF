using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloCaptureScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.LiveSoloProgramCaptures.AsNoTracking().Where(x => x.State == LiveSoloCaptureState.Pending
            || x.State == LiveSoloCaptureState.Starting || x.State == LiveSoloCaptureState.Active || x.State == LiveSoloCaptureState.Stopping
            || x.State == LiveSoloCaptureState.Completed && x.ImportedAt == null
            || x.State == LiveSoloCaptureState.RequiresReview && x.EgressId == null).Select(x => x.MediaSessionId)
            .Union(db.LiveSoloRecordings.AsNoTracking().Where(x => x.State == LiveSoloRecordingState.Pending || x.State == LiveSoloRecordingState.Starting
                || x.State == LiveSoloRecordingState.Recording || x.State == LiveSoloRecordingState.Finalizing
                || x.State == LiveSoloRecordingState.RequiresReview && x.EgressId == null).Select(x => x.MediaSessionId))
            .Union(db.LiveSoloMediaSessions.Where(x => x.State == LiveSoloMediaState.Ready).Select(x => x.Id)).ToArrayAsync(ct);
        var pruning = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.RemoveAfter <= now).Select(x => x.MediaSessionId)
            .Union(db.LiveSoloProgramCaptures.AsNoTracking().Where(x => x.ImportedAt != null && x.RawRemovedAt == null
                && (x.State == LiveSoloCaptureState.Completed || x.State == LiveSoloCaptureState.Failed)).Select(x => x.MediaSessionId))
            .Union(db.LiveSoloRecordings.AsNoTracking().Where(x => !x.DisputeHold && x.KeepUntil <= now).Select(x => x.MediaSessionId)).ToArrayAsync(ct);
        return ids.Select(id => new ClusterScheduleEntry($"live-solo-capture:{id:N}", ClusterScheduleKind.LiveSoloCapture, now,
            TimeSpan.FromSeconds(2), new AdvanceLiveSoloCapture(id)))
            .Concat(pruning.Select(id => new ClusterScheduleEntry($"live-solo-capture-prune:{id:N}", ClusterScheduleKind.LiveSoloCapture, now,
                TimeSpan.FromMinutes(1), new PruneLiveSoloCapture(id)))).ToArray();
    }
}
