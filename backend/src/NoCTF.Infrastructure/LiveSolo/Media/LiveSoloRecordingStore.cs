using System.Data;
using FluentStorage.Storage;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed class LiveSoloRecordingStore(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer, IStore objects, TimeProvider clock)
    : ILiveSoloRecordingStore
{
    private Task<bool> ActiveAsync(Guid actorId, CancellationToken ct) => db.Users.AsNoTracking()
        .AnyAsync(x => x.Id == actorId && x.AccountStatus == UserAccountStatus.Active, ct);
    private Task<bool> MatchAsync(Guid competitionId, Guid matchId, CancellationToken ct) => db.LiveSoloMatches
        .AnyAsync(x => x.Id == matchId && x.CompetitionId == competitionId
            && db.Competitions.Any(c => c.Id == competitionId && c.Mode == GameMode.LiveSolo), ct);
    private async Task<bool> StaffAsync(Guid competitionId, Guid actorId, CancellationToken ct)
        => actorId != Guid.Empty && await ActiveAsync(actorId, ct) && await authorizer.CanObserveAsync(actorId, competitionId, ct);
    private async Task<bool> PublicAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (actorId != Guid.Empty && !await ActiveAsync(actorId, ct)) return false;
        return await db.Competitions.AnyAsync(x => x.Id == competitionId && x.Mode == GameMode.LiveSolo
            && x.Status == CompetitionStatus.Finished && x.AccessMode == CompetitionAccessMode.Public
            && db.Set<LiveSoloCompetitionModeConfiguration>().Any(c => c.CompetitionId == x.Id && c.Enabled), ct);
    }
    private IQueryable<LiveSoloRecording> Records(Guid matchId) => db.LiveSoloRecordings
        .Where(x => db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId));
    private IQueryable<LiveSoloRecordingView> Views(IQueryable<LiveSoloRecording> records) => records.Select(x => new LiveSoloRecordingView(
        x.Id, x.MediaSessionId, x.RoundId, x.UserId,
        db.Users.IgnoreQueryFilters().Where(u => u.Id == x.UserId).Select(u => u.UserName).First(),
        db.Set<LiveSoloMediaParticipant>().Where(p => p.MediaSessionId == x.MediaSessionId && p.UserId == x.UserId).Select(p => (Guid?)p.TeamId).FirstOrDefault(),
        db.Set<LiveSoloMediaParticipant>().Where(p => p.MediaSessionId == x.MediaSessionId && p.UserId == x.UserId)
            .Join(db.Teams.IgnoreQueryFilters(), p => p.TeamId, t => t.Id, (p, t) => t.Name).FirstOrDefault(),
        x.State, x.ConcurrencyStamp, x.CreatedAt, x.StartedAt, x.EndedAt, x.KeepUntil, x.DisputeHold, x.Published,
        db.Files.Where(f => f.Id == x.FileId).Select(f => (long?)f.ByteLength).FirstOrDefault(), x.Failure, x.Chunk));
    public async Task<LiveSoloRecordingPage?> ListAsync(Guid competitionId, Guid matchId, Guid actorId, bool staff, int offset, int limit, CancellationToken ct)
    {
        if (!await MatchAsync(competitionId, matchId, ct)) return null;
        var isStaff = staff && await StaffAsync(competitionId, actorId, ct);
        if (staff && !isStaff || !staff && !await PublicAsync(competitionId, actorId, ct)) return null;
        var now = clock.GetUtcNow();
        var records = Records(matchId).AsNoTracking();
        if (!isStaff) records = records.Where(x => x.Published && x.State == LiveSoloRecordingState.Completed && x.FileId != null
            && (x.DisputeHold || x.KeepUntil > now));
        var total = await records.CountAsync(ct);
        var items = await Views(records.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 100))).ToArrayAsync(ct);
        return new(items, total, isStaff && await authorizer.CanJudgeAsync(actorId, competitionId, ct),
            isStaff && await authorizer.CanModerateAsync(actorId, competitionId, ct));
    }
    public async Task<LiveSoloRecordingView?> ReadRecordingAsync(Guid competitionId,Guid matchId,Guid recordingId,Guid actorId,CancellationToken ct) =>
        await MatchAsync(competitionId,matchId,ct) && await StaffAsync(competitionId,actorId,ct)
            ? await Views(Records(matchId).AsNoTracking().Where(x=>x.Id==recordingId)).SingleOrDefaultAsync(ct) : null;
    public async Task<LiveSoloRecordingChangeResult> ChangeAsync(ChangeLiveSoloRecording command, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct))
                    return new(null, LiveSoloFailure.Forbidden);
                if (command.Action is LiveSoloRecordingAction.Publish or LiveSoloRecordingAction.Withdraw
                    && !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct)) return new(null, LiveSoloFailure.Forbidden);
                if (!await MatchAsync(command.CompetitionId, command.MatchId, ct)) return new(null, LiveSoloFailure.NotFound);
                var record = await Records(command.MatchId).SingleOrDefaultAsync(x => x.Id == command.RecordingId, ct);
                if (record is null) return new(null, LiveSoloFailure.NotFound);
                if (record.ConcurrencyStamp != command.ExpectedStamp) return new(null, LiveSoloFailure.Conflict);
                // Deletion owns the object once this state is committed; a late hold cannot resurrect it.
                if (record.State == LiveSoloRecordingState.Deleting) return new(null, LiveSoloFailure.Conflict);
                var now = clock.GetUtcNow();
                if (command.Action == LiveSoloRecordingAction.Publish && (record.State != LiveSoloRecordingState.Completed || record.FileId is null
                    || !record.DisputeHold && record.KeepUntil <= now
                    || !await db.Competitions.AnyAsync(x => x.Id == command.CompetitionId && x.Status == CompetitionStatus.Finished, ct)
                    || await db.LiveSoloMatches.AnyAsync(x => x.CompetitionId == command.CompetitionId
                        && x.State != LiveSoloMatchState.Completed && x.State != LiveSoloMatchState.Canceled, ct)))
                    return new(null, LiveSoloFailure.NotReady);
                var decision = new LiveSoloRecordingDecision { Id = Guid.CreateVersion7(now), RecordingId = record.Id,
                    MediaSessionId = record.MediaSessionId, ActorUserId = command.ActorId, Action = command.Action, Reason = command.Reason,
                    OccurredAt = now, PreviousHold = record.DisputeHold, PreviousPublished = record.Published };
                switch (command.Action)
                {
                    case LiveSoloRecordingAction.Hold: record.DisputeHold = true; break;
                    case LiveSoloRecordingAction.ReleaseHold: record.DisputeHold = false; break;
                    case LiveSoloRecordingAction.Publish: record.Published = true; break;
                    case LiveSoloRecordingAction.Withdraw: record.Published = false; break;
                    default: return new(null, LiveSoloFailure.InvalidConfiguration);
                }
                decision.Hold = record.DisputeHold; decision.Published = record.Published;
                // A no-op still records the staff reason and consumes the supplied revision.
                record.ConcurrencyStamp = Guid.NewGuid(); db.LiveSoloRecordingDecisions.Add(decision);
                await db.SaveChangesAsync(ct);
                var view = await Views(Records(command.MatchId).Where(x => x.Id == record.Id)).SingleAsync(ct);
                await tx.CommitAsync(ct); return new(view);
            }
            catch (Exception ex) when (attempt < 2 && (ex is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(ex)))
            { db.ChangeTracker.Clear(); }
            catch (Exception ex) when (ex is DbUpdateConcurrencyException || TransactionFailureClassifier.IsRetryable(ex))
            { db.ChangeTracker.Clear(); return new(null, LiveSoloFailure.Conflict); }
        }
    }
    public async Task<bool> MayOpenAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct)
    {
        if (!await MatchAsync(competitionId, matchId, ct)) return false;
        var staff = await StaffAsync(competitionId, actorId, ct);
        if (!staff && !await PublicAsync(competitionId, actorId, ct)) return false;
        var now = clock.GetUtcNow();
        return await Records(matchId).AnyAsync(x => x.Id == recordingId && x.State == LiveSoloRecordingState.Completed
            && x.FileId != null && (x.DisputeHold || x.KeepUntil > now) && (staff || x.Published), ct);
    }
    public async Task<LiveSoloRecordingContent?> OpenAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct)
    {
        if (!await MayOpenAsync(competitionId, matchId, recordingId, actorId, ct)) return null;
        var file = await Records(matchId).AsNoTracking().Where(x => x.Id == recordingId)
            .Join(db.Files.AsNoTracking(), r => r.FileId, f => f.Id, (r, f) => f).SingleOrDefaultAsync(ct);
        if (file is null || !await objects.ObjectExists(file.ObjectKey, ct)) return null;
        var stream = await objects.OpenRead(file.ObjectKey, ct);
        if (stream is null) return null;
        try
        {
            if (!await MayOpenAsync(competitionId, matchId, recordingId, actorId, ct)
                || !await Records(matchId).AnyAsync(x => x.Id == recordingId && x.FileId == file.Id, ct))
            { await stream.DisposeAsync(); return null; }
            return new(file.Id, stream, file.ContentType, $"live-solo-{recordingId:N}.mp4");
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public async Task<Guid?> AuthorizeFileAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct)
        => await MayOpenAsync(competitionId, matchId, recordingId, actorId, ct)
            ? await Records(matchId).AsNoTracking().Where(x => x.Id == recordingId).Select(x => x.FileId).SingleOrDefaultAsync(ct) : null;
    public async Task<IReadOnlyList<LiveSoloRecordingDecisionView>?> DecisionsAsync(Guid competitionId, Guid matchId, Guid recordingId, Guid actorId, CancellationToken ct)
    {
        if (!await MatchAsync(competitionId, matchId, ct) || !await StaffAsync(competitionId, actorId, ct)) return null;
        return await db.LiveSoloRecordingDecisions.AsNoTracking().Where(x => x.RecordingId == recordingId
            && db.LiveSoloMediaSessions.Any(s => s.Id == x.MediaSessionId && s.MatchId == matchId))
            .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Select(x => new LiveSoloRecordingDecisionView(x.Id, x.RecordingId, x.ActorUserId, x.Action, x.Reason, x.OccurredAt,
                x.PreviousHold, x.Hold, x.PreviousPublished, x.Published, x.PreviousState, x.State, x.ReplacementRecordingId)).ToArrayAsync(ct);
    }
}
