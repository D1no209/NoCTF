using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Teams.Moderation;
using System.Data;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloProgramReader(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer, IStore objects, TimeProvider clock) : ILiveSoloProgramReader
{
    private async Task<bool> AllowedAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct)
    {
        if (!await LiveSoloViewerEligibility.AllowedAsync(db, authorizer, competitionId, matchId, actorId, ct)) return false;
        var userId = actorId == Guid.Empty ? (Guid?)null : actorId;
        return await db.Set<LiveSoloViewerLease>().AnyAsync(x => x.Id == leaseId && x.CompetitionId == competitionId
            && x.MatchId == matchId && x.UserId == userId && x.ExpiresAt > clock.GetUtcNow(), ct)
            && await LiveSoloViewerEligibility.WithinCapacityAsync(db, competitionId, leaseId, clock.GetUtcNow(), ct);
    }
    public async Task<LiveSoloProgramView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, Guid leaseId, CancellationToken ct)
    {
        if (!await AllowedAsync(competitionId, matchId, actorId, leaseId, ct)) return null;
        await MarkExposureAsync(competitionId, ct);
        var now = clock.GetUtcNow();
        var terminal=await db.LiveSoloProgramFrames.AsNoTracking().Include(x=>x.Questions).Where(x=>x.Kind==LiveSoloProgramFrameKind.DelayedResult
            &&x.PublicAt!=null&&x.PublicAt<=now&&db.LiveSoloMediaSessions.Any(s=>s.Id==x.MediaSessionId&&s.MatchId==matchId))
            .OrderByDescending(x=>x.OccurredAt).FirstOrDefaultAsync(ct);
        var result=terminal is null?null:new LiveSoloDelayedResultView(terminal.OccurredAt,terminal.PublicAt!.Value,terminal.MatchState,terminal.LeftWins,terminal.RightWins,
            terminal.MatchState==LiveSoloMatchState.Completed?terminal.WinnerTeamId:null,
            terminal.MatchState!=LiveSoloMatchState.Completed?null:terminal.WinnerTeamId==terminal.LeftTeamId?terminal.LeftTeamName:terminal.WinnerTeamId==terminal.RightTeamId?terminal.RightTeamName:null);
        var latest = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.PublicAt <= now && x.RemoveAfter > now
            && db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId))
            .OrderByDescending(x => x.EndedAt).ThenByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        if (latest is null)
        {
            if(terminal is null)return null;
            var endedSession=await db.LiveSoloMediaSessions.AsNoTracking().SingleAsync(x=>x.Id==terminal.MediaSessionId,ct);
            var endedCapture=await db.LiveSoloProgramCaptures.AsNoTracking().Where(x=>x.MediaSessionId==endedSession.Id&&x.NextSegmentSequence>0)
                .OrderByDescending(x=>x.CreatedAt).Select(x=>x.Id).FirstAsync(ct);
            return new(endedCapture,endedSession.PublicDelaySeconds,State(terminal),[],true,result);
        }
        var session = await db.LiveSoloMediaSessions.AsNoTracking().SingleAsync(x => x.Id == latest.MediaSessionId, ct);
        var segments = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.ProgramCaptureId == latest.ProgramCaptureId
            && x.PublicAt <= now && x.RemoveAfter > now).OrderBy(x => x.Sequence)
            .Select(x => new { x.Id, x.Sequence, x.FrameId, x.StartedAt, x.EndedAt }).ToArrayAsync(ct);
        var frameIds = segments.Select(x => x.FrameId).ToArray();
        var frames = await db.LiveSoloProgramFrames.AsNoTracking().Include(x => x.Questions).Where(x => frameIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var frame = frames[latest.FrameId];
        var capture = await db.LiveSoloProgramCaptures.AsNoTracking().SingleAsync(x => x.Id == latest.ProgramCaptureId, ct);
        return new(capture.Id, session.PublicDelaySeconds, State(frame),
            segments.Select(x => new LiveSoloProgramSegmentView(x.Id, x.Sequence, x.EndedAt - x.StartedAt, State(frames[x.FrameId]))).ToArray(), capture.State == LiveSoloCaptureState.Completed,result);
    }
    private static LiveSoloProgramStateView State(LiveSoloProgramFrame frame) => new(frame.OccurredAt, frame.MatchState, frame.RequiredWins, frame.LeftWins, frame.RightWins,
            frame.LeftTeamId, frame.RightTeamId, frame.LeftTeamName, frame.RightTeamName, frame.RoundId, frame.RoundNumber, frame.RoundState,
            frame.TimelineRevision, frame.ActiveElapsedMilliseconds, frame.LimitSeconds, frame.Paused,
            frame.Questions.OrderBy(x => x.Position).Select(x => new LiveSoloProgramQuestionView(x.RoundQuestionId, x.CompetitionChallengeId, x.Position, x.Title, x.OpenedAt)).ToArray());
    public async Task<LiveSoloProgramContent?> OpenSegmentAsync(Guid competitionId, Guid matchId, Guid segmentId, Guid actorId, Guid leaseId, CancellationToken ct)
    {
        if (!await AllowedAsync(competitionId, matchId, actorId, leaseId, ct)) return null;
        var now = clock.GetUtcNow();
        var segment = await db.LiveSoloProgramSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == segmentId && x.PublicAt <= now && x.RemoveAfter > now
            && db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId), ct);
        if (segment is null) return null;
        var file = await db.Files.AsNoTracking().SingleAsync(x => x.Id == segment.FileId, ct);
        if (!await objects.ObjectExists(file.ObjectKey, ct)) return null;
        var stream = await objects.OpenRead(file.ObjectKey, ct);
        if (stream is null) return null;
        if (!await AllowedAsync(competitionId, matchId, actorId, leaseId, ct)
            || !LiveSoloProgramPolicy.MayReadSegment(segment.PublicAt, segment.RemoveAfter, clock.GetUtcNow()))
        { await stream.DisposeAsync(); return null; }
        try { await MarkExposureAsync(competitionId, ct); }
        catch { await stream.DisposeAsync(); throw; }
        return new(stream, file.ContentType);
    }
    private async Task MarkExposureAsync(Guid competitionId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await NoCTF.Infrastructure.LiveSolo.Questions.LiveSoloPublicExposure.RememberAsync(db, competitionId, clock.GetUtcNow(), ct);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            catch (Exception exception) when (attempt < 2 && (exception is DbUpdateException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); }
        }
    }
}
