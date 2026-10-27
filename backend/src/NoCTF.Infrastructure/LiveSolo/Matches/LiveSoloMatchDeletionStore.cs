using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloMatchDeletionResult> DeleteAsync(DeleteLiveSoloMatch command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanModerateAsync(command.ActorId, command.CompetitionId, ct))
                return new LiveSoloMatchDeletionResult([], LiveSoloFailure.Forbidden);
            var target = await db.LiveSoloMatches.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == command.MatchId && x.CompetitionId == command.CompetitionId, ct);
            if (target is null) return new([], LiveSoloFailure.NotFound);
            if (target.DeletedAt != null) return new([]);
            var graph = await db.LiveSoloMatches.Include(x => x.Slots).Include(x => x.Rounds).ThenInclude(x => x.Pauses)
                .Where(x => x.CompetitionId == command.CompetitionId).AsSplitQuery().ToArrayAsync(ct);
            var scope = LiveSoloCorrectionPolicy.Downstream(target.Id, graph).Prepend(target).DistinctBy(x => x.Id).ToArray();
            var ids = scope.Select(x => x.Id).ToArray();
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            var corrections = await db.Set<LiveSoloResultCorrection>().Include(x => x.Matches)
                .Where(x => x.CompetitionId == command.CompetitionId && x.State == LiveSoloCorrectionState.Pending
                    && (ids.Contains(x.MatchId) || x.Matches.Any(m => ids.Contains(m.MatchId)))).ToArrayAsync(ct);
            foreach (var correction in corrections)
            {
                foreach (var match in graph.Where(x => x.PendingCorrectionId == correction.Id))
                {
                    foreach (var round in match.Rounds)
                    {
                        var pauses = round.Pauses.Where(x => x.Source == LiveSoloPauseSource.ResultCorrection && x.EndedAt == null).ToArray();
                        if (pauses.Length == 0) continue;
                        foreach (var pause in pauses) pause.EndedAt = now;
                        round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid();
                        if (!ids.Contains(match.Id) && LiveSoloRoundSchedulePolicy.NextWakeup(round, now) is { } next)
                            await messages.ScheduleAsync(new AdvanceLiveSoloRound(round.Id, round.TimelineRevision, next), next);
                    }
                    var previous = correction.Matches.SingleOrDefault(x => x.MatchId == match.Id);
                    if (previous != null) match.State = previous.PreviousState;
                    match.PendingCorrectionId = null; match.ConcurrencyStamp = Guid.NewGuid();
                }
                correction.State = LiveSoloCorrectionState.Canceled; correction.ResolvedAt = now; correction.ResolvedByUserId = command.ActorId;
                correction.ConcurrencyStamp = Guid.NewGuid();
            }
            foreach (var match in scope)
            {
                foreach (var round in match.Rounds)
                {
                    if (round.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled) continue;
                    round.State = LiveSoloRoundState.Canceled; round.EndedAt = now; round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid();
                    foreach (var pause in round.Pauses.Where(x => x.EndedAt == null)) pause.EndedAt = now;
                    LiveSoloResults.Confirm(round, match, LiveSoloEndReason.Voided);
                }
                match.State = LiveSoloMatchState.Canceled; match.WinnerTeamId = null; match.PendingCorrectionId = null;
                match.CurrentMediaSessionId = null; match.DeletedAt = now; match.DeletedByUserId = command.ActorId;
                match.CompletedAt ??= now; match.ConcurrencyStamp = Guid.NewGuid();
                LiveSoloResults.Confirm(match, LiveSoloEndReason.Voided, now);
                await ReleaseActiveTeamSlotsAsync(match.Id, ct);
            }
            var roundIds = scope.SelectMany(x => x.Rounds).Select(x => x.Id).ToArray();
            var pending = await db.GameplayFacts.Where(x => x.State != GameplayFactState.Completed
                && db.LiveSoloSubmissions.Any(s => roundIds.Contains(s.RoundId) && s.GameplayFactId == x.Id)).ToArrayAsync(ct);
            foreach (var fact in pending)
            {
                fact.State = GameplayFactState.Completed; fact.Result = GameplayFactResult.Rejected;
                fact.FailureCode = GameplayFactFailureCode.RoundOutOfRange; fact.UpdatedAt = now;
            }
            var sessions = await db.LiveSoloMediaSessions.Where(x => ids.Contains(x.MatchId) && x.State != LiveSoloMediaState.Stopped).ToArrayAsync(ct);
            var sessionIds = sessions.Select(x => x.Id).ToArray();
            foreach (var grant in await db.LiveSoloMediaGrants.Where(x => sessionIds.Contains(x.MediaSessionId) && x.RevokedAt == null).ToArrayAsync(ct)) grant.RevokedAt = now;
            foreach (var session in sessions)
            {
                session.State = LiveSoloMediaState.Stopping;
                await messages.PublishAsync(new RefreshLiveSoloMedia(session.Id, session.RoomIdentity));
            }
            (await db.Competitions.SingleAsync(x => x.Id == command.CompetitionId, ct)).ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            foreach (var fact in pending) await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State, fact.ConcurrencyStamp));
            return new(ids);
        }, () => new([], LiveSoloFailure.Conflict), ct);
        if (result.Failure is null)
        {
            var ids = result.MatchIds.ToArray();
            foreach (var roundId in await db.LiveSoloRounds.Where(x => ids.Contains(x.MatchId)).Select(x => x.Id).ToArrayAsync(ct))
                await runtimePreparation.StopRoundAsync(roundId, command.Now, ct);
        }
        return result;
    }
}
