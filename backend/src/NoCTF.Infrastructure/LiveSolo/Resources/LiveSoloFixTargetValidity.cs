using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.LiveSolo.Rounds;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Resources;

public static class LiveSoloFixTargetValidity
{
    public static async Task<bool> CanProvisionAsync(NoCtfDbContext db, RuntimeInstance runtime, DateTimeOffset now, CancellationToken ct)
    {
        var scope = await db.Set<LiveSoloFixTargetOwnership>().AsNoTracking().Where(value => value.RuntimeInstanceId == runtime.Id)
            .Join(db.LiveSoloRoundQuestions.AsNoTracking(), value => value.RoundQuestionId, value => value.Id, (binding, question) => new { binding.Side, Question = question })
            .Join(db.LiveSoloRounds.AsNoTracking().Include(value => value.Pauses), value => value.Question.RoundId, value => value.Id,
                (value, round) => new { value.Side, value.Question, Round = round })
            .Join(db.LiveSoloMatches.AsNoTracking().Include(value => value.Slots), value => value.Round.MatchId, value => value.Id,
                (value, match) => new { value.Side, value.Question, value.Round, Match = match })
            .SingleOrDefaultAsync(ct);
        if (scope is null || scope.Match.CompetitionId != runtime.CompetitionId || scope.Question.CompetitionChallengeId != runtime.CompetitionChallengeId
            || !scope.Match.Slots.Any(value => value.Side == scope.Side && value.TeamId == runtime.TeamId)
            || scope.Question.OpenedAt is null || scope.Question.OpenedAt > now || scope.Match.CurrentRoundId != scope.Round.Id
            || scope.Match.PendingCorrectionId is not null || scope.Match.SupersededAt is not null
            || scope.Match.State is not (LiveSoloMatchState.Running or LiveSoloMatchState.Paused)
            || scope.Round.State is not (LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)) return false;
        if (!await db.Competitions.AnyAsync(value => value.Id == runtime.CompetitionId
                && (value.Status == CompetitionStatus.Running || value.Status == CompetitionStatus.Paused), ct)) return false;
        if (runtime.GameplayFactId is { } factId)
            return await db.GameplayFacts.AnyAsync(fact => fact.Id == factId && fact.CompetitionId == runtime.CompetitionId
                && fact.CompetitionChallengeId == runtime.CompetitionChallengeId && fact.TeamId == runtime.TeamId
                && (fact.State == GameplayFactState.Pending || fact.State == GameplayFactState.Queued || fact.State == GameplayFactState.Processing)
                && fact.FixExecution != null && fact.FixExecution.RuntimeInstanceId == runtime.Id && fact.FixExecution.Deadline > now
                && db.LiveSoloSubmissions.Any(value => value.GameplayFactId == fact.Id && value.RoundQuestionId == scope.Question.Id), ct);
        if (scope.Round.State != LiveSoloRoundState.Running || scope.Round.StartedAt is not { } started) return false;
        var transitions = await LiveSoloCompetitionPauseReader.ReadAsync(db, [scope.Match.CompetitionId], now, ct);
        scope.Round.Pauses.RemoveAll(value => value.Source == LiveSoloPauseSource.Competition);
        scope.Round.Pauses.AddRange(LiveSoloCompetitionPausePolicy.Project(scope.Round.Id, scope.Round.CreatedAt, now,
            transitions.GetValueOrDefault(scope.Match.CompetitionId, [])));
        return LiveSoloActiveClock.Elapsed(started, now, scope.Round.Pauses) < TimeSpan.FromSeconds(scope.Round.LimitSeconds);
    }
}
