using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Infrastructure.Persistence;
using FluentStorage.Storage;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloCaptureStore(NoCtfDbContext db, ILiveSoloEgressGateway egress, ILiveSoloCaptureFiles files,
    ManagedFileUploads uploads, LiveKitMediaOptions options, TimeProvider clock, IPostCommitMessagePublisher messages, IStore objects) : ILiveSoloCaptureStore
{
    private async Task TransactionAsync(Func<Task> work, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await work(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
            }
            catch (Exception exception) when (attempt < 2 && (exception is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(exception)))
            { db.ChangeTracker.Clear(); }
        }
    }
    public async Task EnsureAsync(Guid sessionId, CancellationToken ct)
    {
        await TransactionAsync(async () =>
        {
            var session = await db.LiveSoloMediaSessions.Include(x => x.Participants).SingleOrDefaultAsync(x => x.Id == sessionId, ct);
            if (session is null || session.State != LiveSoloMediaState.Ready
                || !await db.LiveSoloMatches.AnyAsync(x => x.Id == session.MatchId && x.CurrentMediaSessionId == session.Id, ct)) return;
            if (session.CurrentProgramCaptureId is null)
            {
                var program = new LiveSoloProgramCapture { Id = Guid.CreateVersion7(clock.GetUtcNow()), MediaSessionId = session.Id, CreatedAt = clock.GetUtcNow() };
                db.LiveSoloProgramCaptures.Add(program); await db.SaveChangesAsync(ct);
                session.CurrentProgramCaptureId = program.Id;
            }
            if (session.RecordingEnabled)
            {
                foreach (var member in session.Participants.Where(x => x.ScreenState == LiveSoloScreenState.Sharing && x.ScreenTrackId != null))
                {
                    var latest = await db.LiveSoloRecordings.Where(x=>x.MediaSessionId==session.Id&&x.UserId==member.UserId&&x.VideoTrackId==member.ScreenTrackId)
                        .OrderByDescending(x=>x.Chunk).FirstOrDefaultAsync(ct);
                    if (latest is not null && (latest.State!=LiveSoloRecordingState.Completed || latest.RawRemovedAt is null)) continue;
                    db.LiveSoloRecordings.Add(new() { Id = Guid.CreateVersion7(clock.GetUtcNow()), MediaSessionId = session.Id,
                        UserId = member.UserId, VideoTrackId = member.ScreenTrackId!, State = LiveSoloRecordingState.Pending, CreatedAt = clock.GetUtcNow(),
                        Chunk=(latest?.Chunk??-1)+1,
                        KeepUntil = clock.GetUtcNow().AddDays(session.RecordingRetentionDays) });
                }
            }
            await FrameAsync(session, ct);
        }, ct);
    }
    private async Task FrameAsync(LiveSoloMediaSession session, CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        if (await db.LiveSoloProgramFrames.AnyAsync(x => x.MediaSessionId == session.Id && x.OccurredAt == at, ct)) return;
        var match = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).SingleAsync(x => x.Id == session.MatchId, ct);
        var round = match.CurrentRoundId is Guid roundId ? await db.LiveSoloRounds.AsNoTracking().Include(x => x.Pauses).SingleOrDefaultAsync(x => x.Id == roundId, ct) : null;
        var teams = match.Slots.Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).ToArray();
        var names = await db.Teams.IgnoreQueryFilters().Where(x => teams.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        if (round is not null)
        {
            var transitions = await LiveSoloCompetitionPauseReader.ReadAsync(db, [match.CompetitionId], at, ct);
            round.Pauses = round.Pauses.Where(x => x.Source != LiveSoloPauseSource.Competition)
                .Concat(LiveSoloCompetitionPausePolicy.Project(round.Id, round.CreatedAt, at, transitions.GetValueOrDefault(match.CompetitionId, []))).ToList();
        }
        var left = match.Slots.Single(x => x.Side == LiveSoloSide.Left).TeamId;
        var right = match.Slots.Single(x => x.Side == LiveSoloSide.Right).TeamId;
        var frame = new LiveSoloProgramFrame { Id = Guid.CreateVersion7(at), MediaSessionId = session.Id, OccurredAt = at,
            MatchState = match.State, RequiredWins = match.RequiredWins, LeftWins = match.LeftWins, RightWins = match.RightWins,
            LeftTeamId = left, RightTeamId = right, LeftTeamName = left is Guid l ? names.GetValueOrDefault(l) : null,
            RightTeamName = right is Guid r ? names.GetValueOrDefault(r) : null, RoundId = round?.Id, RoundNumber = round?.Number,
            RoundState = round?.State, TimelineRevision = round?.TimelineRevision, LimitSeconds = round?.LimitSeconds,
            Paused = round is not null && LiveSoloActiveClock.Paused(round.Pauses), ActiveElapsedMilliseconds = round?.StartedAt is { } started
                ? Math.Min(round.LimitSeconds * 1000L, (long)LiveSoloActiveClock.Elapsed(started, round.EndedAt ?? at, round.Pauses).TotalMilliseconds) : 0 };
        if (round is not null)
        {
            frame.Questions = await db.LiveSoloRoundQuestions.AsNoTracking().Where(x => x.RoundId == round.Id && x.OpenedAt != null && x.OpenedAt <= at)
                .Join(db.CompetitionChallenges, q => q.CompetitionChallengeId, c => c.Id, (q, c) => new { Question = q, c.ChallengeId })
                .Join(db.Challenges, x => x.ChallengeId, c => c.Id, (x, c) => new LiveSoloProgramFrameQuestion { FrameId = frame.Id,
                    Position = x.Question.Position, RoundQuestionId = x.Question.Id, CompetitionChallengeId = x.Question.CompetitionChallengeId,
                    CanonicalChallengeId = db.LiveSoloChallengeSources.Where(s => s.ChallengeId == c.Id).Select(s => (Guid?)s.CanonicalChallengeId).FirstOrDefault() ?? c.Id,
                    HasStaticAnswer = db.ChallengeFlags.Any(flag => flag.DeletedAt == null && (flag.Type == NoCTF.Domain.Challenges.ChallengeFlagType.Template && flag.ChallengeId == c.Id
                        || flag.Type == NoCTF.Domain.Challenges.ChallengeFlagType.Competition && flag.CompetitionChallengeId == x.Question.CompetitionChallengeId
                        || flag.Type == NoCTF.Domain.Challenges.ChallengeFlagType.Team && flag.CompetitionChallengeId == x.Question.CompetitionChallengeId
                            && flag.SpecificationKind == NoCTF.Domain.Challenges.SpecificationKind.Attachment)),
                    Title = c.Title, OpenedAt = x.Question.OpenedAt!.Value }).OrderBy(x => x.Position).ToListAsync(ct);
        }
        db.LiveSoloProgramFrames.Add(frame);
    }
}
