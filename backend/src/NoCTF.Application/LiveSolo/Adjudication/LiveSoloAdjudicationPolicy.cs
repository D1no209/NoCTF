using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Adjudication;

public static class LiveSoloAdjudicationPolicy
{
    public static LiveSoloFailure? CanApply(LiveSoloMatch match, LiveSoloRound? round, AdjudicateLiveSoloMatch command)
    {
        if (match.PendingCorrectionId is not null) return LiveSoloFailure.NotReady;
        if (match.ConcurrencyStamp != command.ExpectedMatchStamp || match.CurrentRoundId != command.ExpectedRoundId
            || round?.Id != command.ExpectedRoundId || round is not null && round.MatchId != match.Id
            || round?.ConcurrencyStamp != command.ExpectedRoundStamp || round?.TimelineRevision != command.ExpectedTimelineRevision)
            return LiveSoloFailure.Conflict;
        if (match.State is LiveSoloMatchState.Completed or LiveSoloMatchState.Canceled) return LiveSoloFailure.AlreadyEnded;
        return command.Action switch
        {
            LiveSoloJudgeAction.Pause => round?.State is LiveSoloRoundState.Countdown or LiveSoloRoundState.Running
                && match.State is LiveSoloMatchState.Countdown or LiveSoloMatchState.Running ? null : LiveSoloFailure.RoundNotRunning,
            LiveSoloJudgeAction.Resume => round?.State is LiveSoloRoundState.Countdown or LiveSoloRoundState.Running
                && match.State == LiveSoloMatchState.Paused
                && round.Pauses.Any(x => x.Source == LiveSoloPauseSource.Match && x.EndedAt == null) ? null : LiveSoloFailure.RoundNotRunning,
            LiveSoloJudgeAction.VoidRound => round?.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown
                or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult ? null : LiveSoloFailure.AlreadyEnded,
            LiveSoloJudgeAction.ForfeitMatch => match.Slots.Count == 2 && match.Slots.All(x => x.Resolved && x.TeamId is not null)
                && match.Slots.Any(x => x.TeamId == command.ForfeitingTeamId) ? null : LiveSoloFailure.InvalidConfiguration,
            _ => LiveSoloFailure.InvalidConfiguration
        };
    }

    public static LiveSoloAdjudication Apply(LiveSoloMatch match, LiveSoloRound? round, AdjudicateLiveSoloMatch command, DateTimeOffset now)
    {
        if (CanApply(match, round, command) is not null) throw new InvalidOperationException("The decision must target the current eligible revision.");
        var decision = new LiveSoloAdjudication { Id = Guid.CreateVersion7(now), MatchId = match.Id, RoundId = round?.Id,
            ActorUserId = command.ActorId, ForfeitingTeamId = command.ForfeitingTeamId, Action = command.Action, Reason = command.Reason,
            OccurredAt = now, PreviousMatchState = match.State, PreviousRoundState = round?.State,
            PreviousLeftWins = match.LeftWins, PreviousRightWins = match.RightWins, PreviousTimelineRevision = round?.TimelineRevision };
        switch (command.Action)
        {
            case LiveSoloJudgeAction.Pause:
                round!.Pauses.Add(new() { Id = Guid.CreateVersion7(now), RoundId = round.Id, Source = LiveSoloPauseSource.Match, StartedAt = now });
                match.State = LiveSoloMatchState.Paused;
                break;
            case LiveSoloJudgeAction.Resume:
                CloseMatchPauses(round!, now);
                match.State = round!.State == LiveSoloRoundState.Countdown ? LiveSoloMatchState.Countdown : LiveSoloMatchState.Running;
                break;
            case LiveSoloJudgeAction.VoidRound:
                CancelRound(round!, now, command.Reason); match.State = LiveSoloMatchState.Preparing;
                foreach (var slot in match.Slots) slot.ReadyConfirmedAt = null;
                break;
            case LiveSoloJudgeAction.ForfeitMatch:
                var winner = match.Slots.Single(x => x.TeamId != command.ForfeitingTeamId);
                if (round?.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown or LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)
                    CancelRound(round, now, command.Reason);
                match.WinnerTeamId = winner.TeamId; match.State = LiveSoloMatchState.Completed; match.CompletedAt = now;
                break;
        }
        match.AdjudicationReason = command.Reason; match.ConcurrencyStamp = Guid.NewGuid();
        if (round is not null) { round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid(); }
        decision.MatchState = match.State; decision.RoundState = round?.State; decision.LeftWins = match.LeftWins;
        decision.RightWins = match.RightWins; decision.TimelineRevision = round?.TimelineRevision;
        return decision;
    }

    private static void CloseMatchPauses(LiveSoloRound round, DateTimeOffset now)
    {
        foreach (var pause in round.Pauses.Where(x => x.Source == LiveSoloPauseSource.Match && x.EndedAt == null)) pause.EndedAt = now;
    }
    private static void CancelRound(LiveSoloRound round, DateTimeOffset now, string reason)
    {
        CloseMatchPauses(round, now);
        round.State = LiveSoloRoundState.Canceled; round.EndedAt = now; round.AdjudicationReason = reason;
    }
}
