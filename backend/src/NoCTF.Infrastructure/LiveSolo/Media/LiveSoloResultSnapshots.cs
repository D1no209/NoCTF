using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore
{
    public async Task SnapshotResultAsync(Guid matchId,CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await TransactionAsync(async () =>
                {
                    var match = await db.LiveSoloMatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == matchId
                        && (x.State == LiveSoloMatchState.Completed || x.State == LiveSoloMatchState.Canceled), ct);
                    if (match is null) return;
                    foreach (var session in await db.LiveSoloMediaSessions.AsNoTracking().Where(x => x.MatchId == match.Id
                        && db.LiveSoloProgramCaptures.Any(c => c.MediaSessionId == x.Id && c.NextSegmentSequence > 0)).ToArrayAsync(ct))
                        await FrameAsync(session, ct, LiveSoloProgramFrameKind.DelayedResult);
                }, ct);
                return;
            }
            catch (DbUpdateException exception) when (attempt < 2
                && exception.GetBaseException() is DbException { SqlState: "23505" }
                && exception.Entries.Count > 0
                && exception.Entries.All(entry => entry.Entity is LiveSoloProgramFrame or LiveSoloProgramFrameQuestion))
            {
                // Reread the concurrent committed snapshot before retrying this idempotent database work.
                db.ChangeTracker.Clear();
            }
        }
    }
}
