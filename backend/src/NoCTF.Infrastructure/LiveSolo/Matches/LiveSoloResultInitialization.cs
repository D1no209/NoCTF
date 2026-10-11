using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private Task<bool> InitializeResultsAsync(Guid competitionId, Guid matchId, CancellationToken ct) => TransactionAsync(async () =>
    {
        var match = await db.LiveSoloMatches.Include(x => x.Rounds).SingleOrDefaultAsync(x => x.Id == matchId && x.CompetitionId == competitionId, ct);
        if (match is null) return false;
        var decisions = await db.Set<LiveSoloAdjudication>().AsNoTracking().Where(x => x.MatchId == match.Id).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToArrayAsync(ct);
        foreach (var round in match.Rounds.Where(x => x.ResultRevision == 0 && x.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled))
        {
            round.ResultRevision = 1;
            var decision = decisions.LastOrDefault(x => x.RoundId == round.Id && x.RoundState == round.State
                && x.Action is LiveSoloJudgeAction.VoidRound or LiveSoloJudgeAction.ForfeitMatch);
            if (decision != null)
            {
                round.EndReason = decision.Action == LiveSoloJudgeAction.ForfeitMatch ? LiveSoloEndReason.Forfeit : LiveSoloEndReason.Voided;
                round.ResultLeftWins = decision.LeftWins; round.ResultRightWins = decision.RightWins;
            }
            else if (round.State == LiveSoloRoundState.Won && round.WinningGameplayFactId is Guid factId
                && await db.GameplayFacts.AnyAsync(x => x.Id == factId && x.TeamId == round.WinnerTeamId && x.State == GameplayFactState.Completed
                    && x.Result == GameplayFactResult.Correct && db.LiveSoloSubmissions.Any(s => s.GameplayFactId == x.Id && s.RoundId == round.Id), ct))
                round.EndReason = LiveSoloEndReason.CorrectAnswer;
            else if (round.State == LiveSoloRoundState.TimedOut && round.StartedAt != null && round.EndedAt != null
                && round.WinnerTeamId == null) round.EndReason = LiveSoloEndReason.TimeLimit;
            // Unknown historical cause and end score remain explicitly unrecorded; no winner is inferred.
        }
        if (match.ResultRevision == 0 && match.State is LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled)
        {
            match.ResultRevision = 1;
            var correction = await db.Set<LiveSoloResultCorrection>().AsNoTracking().Where(x => x.MatchId == match.Id && x.State == LiveSoloCorrectionState.Applied)
                .OrderByDescending(x => x.ResolvedAt).FirstOrDefaultAsync(ct);
            var forfeit = decisions.LastOrDefault(x => x.Action == LiveSoloJudgeAction.ForfeitMatch && x.MatchState == LiveSoloMatchState.Completed);
            if (correction != null && correction.WinnerTeamId == match.WinnerTeamId && correction.LeftWins == match.LeftWins && correction.RightWins == match.RightWins)
            { match.EndReason = LiveSoloEndReason.ResultCorrected; match.ResultUpdatedAt = correction.ResolvedAt; match.ResultLeftWins = correction.LeftWins; match.ResultRightWins = correction.RightWins; }
            else if (forfeit != null && match.State == LiveSoloMatchState.Completed)
            { match.EndReason = LiveSoloEndReason.Forfeit; match.ResultUpdatedAt = forfeit.OccurredAt; match.ResultLeftWins = forfeit.LeftWins; match.ResultRightWins = forfeit.RightWins; }
            else if (match.State == LiveSoloMatchState.Completed && match.Rounds.Any(x => x.State == LiveSoloRoundState.Won
                && x.WinnerTeamId == match.WinnerTeamId && x.EndReason == LiveSoloEndReason.CorrectAnswer)
                && Math.Max(match.LeftWins, match.RightWins) == match.RequiredWins)
            { match.EndReason = LiveSoloEndReason.RequiredWinsReached; match.ResultUpdatedAt = match.CompletedAt; match.ResultLeftWins = match.LeftWins; match.ResultRightWins = match.RightWins; }
            else if (match.SupersededAt != null && match.ReplacementMatchId != null)
            { match.EndReason = LiveSoloEndReason.DownstreamReplaced; match.ResultUpdatedAt = match.SupersededAt; }
        }
        await db.SaveChangesAsync(ct); return true;
    }, () => false, ct);
}
