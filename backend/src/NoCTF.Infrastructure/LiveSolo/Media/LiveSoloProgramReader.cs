using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using System.Data;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloProgramReader(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer, IStore objects, TimeProvider clock) : ILiveSoloProgramReader
{
    private async Task<bool> AllowedAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId && x.Mode == GameMode.LiveSolo, ct);
        if (competition is null || competition.Status is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == competitionId && x.Enabled, ct)
            || !await db.LiveSoloMatches.AnyAsync(x => x.Id == matchId && x.CompetitionId == competitionId && x.StartedAt != null, ct)) return false;
        return competition.AccessMode == CompetitionAccessMode.Public || actorId != Guid.Empty && await authorizer.CanObserveAsync(actorId, competitionId, ct);
    }
    public async Task<LiveSoloProgramView?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        if (!await AllowedAsync(competitionId, matchId, actorId, ct)) return null;
        var now = clock.GetUtcNow();
        var latest = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.PublicAt <= now && x.RemoveAfter > now
            && db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId))
            .OrderByDescending(x => x.EndedAt).ThenByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        if (latest is null) return null;
        var session = await db.LiveSoloMediaSessions.AsNoTracking().SingleAsync(x => x.Id == latest.MediaSessionId, ct);
        var segments = await db.LiveSoloProgramSegments.AsNoTracking().Where(x => x.ProgramCaptureId == latest.ProgramCaptureId
            && x.PublicAt <= now && x.RemoveAfter > now).OrderBy(x => x.Sequence)
            .Select(x => new { x.Id, x.Sequence, x.FrameId, x.StartedAt, x.EndedAt }).ToArrayAsync(ct);
        var frameIds = segments.Select(x => x.FrameId).ToArray();
        var frames = await db.LiveSoloProgramFrames.AsNoTracking().Include(x => x.Questions).Where(x => frameIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var frame = frames[latest.FrameId];
        var capture = await db.LiveSoloProgramCaptures.AsNoTracking().SingleAsync(x => x.Id == latest.ProgramCaptureId, ct);
        return new(capture.Id, session.PublicDelaySeconds, State(frame),
            segments.Select(x => new LiveSoloProgramSegmentView(x.Id, x.Sequence, x.EndedAt - x.StartedAt, State(frames[x.FrameId]))).ToArray(), capture.State == LiveSoloCaptureState.Completed);
    }
    private static LiveSoloProgramStateView State(LiveSoloProgramFrame frame) => new(frame.OccurredAt, frame.MatchState, frame.RequiredWins, frame.LeftWins, frame.RightWins,
            frame.LeftTeamId, frame.RightTeamId, frame.LeftTeamName, frame.RightTeamName, frame.RoundId, frame.RoundNumber, frame.RoundState,
            frame.TimelineRevision, frame.ActiveElapsedMilliseconds, frame.LimitSeconds, frame.Paused,
            frame.Questions.OrderBy(x => x.Position).Select(x => new LiveSoloProgramQuestionView(x.RoundQuestionId, x.CompetitionChallengeId, x.Position, x.Title, x.OpenedAt)).ToArray());
    public async Task<LiveSoloProgramContent?> OpenSegmentAsync(Guid competitionId, Guid matchId, Guid segmentId, Guid actorId, CancellationToken ct)
    {
        if (!await AllowedAsync(competitionId, matchId, actorId, ct)) return null;
        var now = clock.GetUtcNow();
        var segment = await db.LiveSoloProgramSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == segmentId && x.PublicAt <= now && x.RemoveAfter > now
            && db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId), ct);
        if (segment is null) return null;
        var file = await db.Files.AsNoTracking().SingleAsync(x => x.Id == segment.FileId, ct);
        if (!await objects.ObjectExists(file.ObjectKey, ct)) return null;
        var stream = await objects.OpenRead(file.ObjectKey, ct);
        if (stream is null) return null;
        if (!await AllowedAsync(competitionId, matchId, actorId, ct)
            || !LiveSoloProgramPolicy.MayReadSegment(segment.PublicAt, segment.RemoveAfter, clock.GetUtcNow()))
        { await stream.DisposeAsync(); return null; }
        try { await MarkExposureAsync(competitionId, matchId, segment.FrameId, ct); }
        catch { await stream.DisposeAsync(); throw; }
        return new(stream, file.ContentType);
    }
    private async Task MarkExposureAsync(Guid competitionId, Guid matchId, Guid frameId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var questionIds = await db.Set<LiveSoloProgramFrameQuestion>().Where(x => x.FrameId == frameId).Select(x => x.CompetitionChallengeId).ToArrayAsync(ct);
                var templates = await db.CompetitionChallenges.Where(x => questionIds.Contains(x.Id)
                    && db.ChallengeFlags.Any(flag => (flag.Type == ChallengeFlagType.Template && flag.ChallengeId == x.ChallengeId
                        || flag.Type == ChallengeFlagType.Competition && flag.CompetitionChallengeId == x.Id) && flag.DeletedAt == null))
                    .Select(x => x.ChallengeId).Distinct().ToArrayAsync(ct);
                var canonical = await db.LiveSoloChallengeSources.Where(x => templates.Contains(x.ChallengeId)).ToDictionaryAsync(x => x.ChallengeId, x => x.CanonicalChallengeId, ct);
                foreach (var id in templates.Select(id => canonical.GetValueOrDefault(id, id)).Distinct())
                    if (!await db.LiveSoloQuestionExposures.AnyAsync(x => x.CompetitionId == competitionId && x.CanonicalChallengeId == id, ct))
                        db.LiveSoloQuestionExposures.Add(new() { CompetitionId = competitionId, CanonicalChallengeId = id, MatchId = matchId, PublicAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            catch (Exception exception) when (attempt < 2 && (exception is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); }
        }
    }
}
