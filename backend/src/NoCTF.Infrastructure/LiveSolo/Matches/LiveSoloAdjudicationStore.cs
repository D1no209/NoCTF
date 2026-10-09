using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Adjudication;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    public async Task<LiveSoloAdjudicationResult> ApplyAsync(AdjudicateLiveSoloMatch command, CancellationToken ct)
    {
        var result = await TransactionAsync(async () =>
        {
            if (!await ActiveAsync(command.ActorId, ct) || !await authorizer.CanJudgeAsync(command.ActorId, command.CompetitionId, ct))
                return new LiveSoloAdjudicationResult(null, null, null, LiveSoloFailure.Forbidden);
            var match = await MatchAsync(command.CompetitionId, command.MatchId, ct);
            if (match is null) return new(null, null, null, LiveSoloFailure.NotFound);
            var round = match.CurrentRoundId is Guid id
                ? await db.LiveSoloRounds.Include(x => x.Pauses).Include(x => x.Questions).SingleAsync(x => x.Id == id, ct) : null;
            if (LiveSoloAdjudicationPolicy.CanApply(match, round, command) is { } failure) return new(null, null, null, failure);
            var persistedPauseIds = round?.Pauses.Select(x => x.Id).ToHashSet() ?? [];
            var effectiveAt = (clock ?? TimeProvider.System).GetUtcNow();
            var decision = LiveSoloAdjudicationPolicy.Apply(match, round, command, effectiveAt);
            if (round is not null)
            {
                db.Set<LiveSoloPauseInterval>().AddRange(round.Pauses.Where(x => !persistedPauseIds.Contains(x.Id)));
                await SynchronizeCompetitionPausesAsync(round, match.CompetitionId, effectiveAt, true, ct);
                decision.TimelineRevision = round.TimelineRevision;
                if (round.State == LiveSoloRoundState.Canceled)
                {
                    var pending = await db.GameplayFacts.Where(x => db.LiveSoloSubmissions.Any(s => s.RoundId == round.Id && s.GameplayFactId == x.Id)
                        && x.State != GameplayFactState.Completed).ToArrayAsync(ct);
                    foreach (var fact in pending)
                    {
                        fact.State = GameplayFactState.Completed; fact.Result = GameplayFactResult.Rejected;
                        fact.FailureCode = GameplayFactFailureCode.RoundOutOfRange; fact.UpdatedAt = effectiveAt;
                        await messages.PublishAsync(new GameplayFactStateChanged(fact.Id, fact.State));
                    }
                }
                else if (command.Action == LiveSoloJudgeAction.Resume && LiveSoloRoundSchedulePolicy.NextWakeup(round, effectiveAt) is { } next)
                    await messages.ScheduleAsync(new AdvanceLiveSoloRound(round.Id, round.TimelineRevision, next), next);
            }
            db.LiveSoloAdjudications.Add(decision);
            if (match.State == LiveSoloMatchState.Completed)
            {
                await ReleaseActiveTeamSlotsAsync(match.Id, ct);
                await AdvanceBracketAsync(match, effectiveAt, ct);
            }
            await db.SaveChangesAsync(ct);
            return new(await MapAsync(match, ct), round is null ? null : Round(round, effectiveAt), Decision(decision));
        }, () => new(null, null, null, LiveSoloFailure.Conflict), ct);
        if (result.Failure is null && command.Action is LiveSoloJudgeAction.VoidRound or LiveSoloJudgeAction.ForfeitMatch)
        {
            var rounds = command.Action == LiveSoloJudgeAction.ForfeitMatch
                ? await db.LiveSoloRounds.Where(x => x.MatchId == command.MatchId).Select(x => x.Id).ToArrayAsync(ct)
                : result.Round is { } canceled ? [canceled.Id] : Array.Empty<Guid>();
            foreach (var roundId in rounds) await runtimePreparation.StopRoundAsync(roundId, (clock ?? TimeProvider.System).GetUtcNow(), ct);
        }
        return result;
    }

    public async Task<IReadOnlyList<LiveSoloAdjudicationView>?> ReadAsync(Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        if (!await ActiveAsync(actorId, ct) || !await authorizer.CanObserveAsync(actorId, competitionId, ct)
            || !await db.LiveSoloMatches.AnyAsync(x => x.Id == matchId && x.CompetitionId == competitionId, ct)) return null;
        return (await db.LiveSoloAdjudications.AsNoTracking().Where(x => x.MatchId == matchId).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            .ToArrayAsync(ct)).Select(Decision).ToArray();
    }
    private static LiveSoloAdjudicationView Decision(LiveSoloAdjudication x) => new(x.Id, x.MatchId, x.RoundId, x.ActorUserId, x.ForfeitingTeamId,
        x.Action, x.Reason, x.OccurredAt, x.PreviousMatchState, x.MatchState, x.PreviousRoundState, x.RoundState,
        x.PreviousLeftWins, x.PreviousRightWins, x.LeftWins, x.RightWins, x.PreviousTimelineRevision, x.TimelineRevision);

    private async Task ReleaseActiveTeamSlotsAsync(Guid matchId, CancellationToken ct)
    {
        await db.LiveSoloActiveTeamSlots.Where(x => x.MatchId == matchId).ExecuteDeleteAsync(ct);
        foreach (var slot in db.ChangeTracker.Entries<LiveSoloActiveTeamSlot>().Where(x => x.Entity.MatchId == matchId).ToArray())
            slot.State = EntityState.Detached;
    }
}
