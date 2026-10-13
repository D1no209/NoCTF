using NoCTF.Domain.Gameplay;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Rounds;

public enum LiveSoloFailure : short
{
    NotFound, Forbidden, Disabled, InvalidConfiguration, Conflict, RosterLocked, NotReady,
    MediaUnavailable, IsolationUnavailable, RoundNotRunning, RoundPaused, QuestionNotOpen,
    DeadlinePassed, AlreadyEnded, NoSuitableQuestionGroup, DependencyUnavailable, IdempotencyConflict, CompetitionNotStarted, ResourceCapacityExceeded, UnsupportedEnvironment,
    IsolationNotConfigured, IsolationVerificationFailed, QuestionGroupNotFound, SelectionChanged, ActiveQuestionConflict
}
public enum LiveSoloResolution : short { Waiting, NoWinner, Winner }
public sealed record LiveSoloOrderedResult(long Sequence, Guid GameplayFactId, Guid TeamId,
    GameplayFactState State, GameplayFactResult? Result);
public sealed record LiveSoloResolutionDecision(LiveSoloResolution State, long ResolvedThrough,
    Guid? WinnerTeamId = null, Guid? WinningGameplayFactId = null);

/// <summary>Resolves an admission-ordered stream without sorting each growing prefix again.</summary>
public sealed class LiveSoloOrderedResolution(long admittedThrough)
{
    private long next = 1;
    private LiveSoloResolutionDecision? terminal;

    public LiveSoloResolutionDecision Decision => terminal
        ?? new(next <= admittedThrough ? LiveSoloResolution.Waiting : LiveSoloResolution.NoWinner, next - 1);

    public void Observe(LiveSoloOrderedResult result)
    {
        if (terminal is not null) return;
        if (result.Sequence != next || result.State != GameplayFactState.Completed || result.Result is null)
            terminal = new(LiveSoloResolution.Waiting, next - 1);
        else if (result.Result == GameplayFactResult.Correct)
            terminal = new(LiveSoloResolution.Winner, result.Sequence, result.TeamId, result.GameplayFactId);
        else
            next = checked(next + 1);
    }
}

public static class LiveSoloRoundRules
{
    public static LiveSoloFailure? CanAdmit(LiveSoloRound round, LiveSoloRoundQuestion question,
        DateTimeOffset now)
    {
        if (round.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled)
            return LiveSoloFailure.AlreadyEnded;
        if (round.State != LiveSoloRoundState.Running || round.StartedAt is null)
            return LiveSoloFailure.RoundNotRunning;
        if (LiveSoloActiveClock.Paused(round.Pauses)) return LiveSoloFailure.RoundPaused;
        if (question.RoundId != round.Id || question.OpenedAt is null || question.OpenedAt > now)
            return LiveSoloFailure.QuestionNotOpen;
        return LiveSoloActiveClock.Elapsed(round.StartedAt.Value, now, round.Pauses) >= TimeSpan.FromSeconds(round.LimitSeconds)
            ? LiveSoloFailure.DeadlinePassed : null;
    }

    public static LiveSoloResolutionDecision Resolve(long admittedThrough, IEnumerable<LiveSoloOrderedResult> results)
    {
        var resolution = new LiveSoloOrderedResolution(admittedThrough);
        foreach (var result in results.OrderBy(x => x.Sequence))
            resolution.Observe(result);
        return resolution.Decision;
    }

    public static bool CanRelease(LiveSoloRound round, LiveSoloRoundQuestion question, DateTimeOffset now) =>
        round.State == LiveSoloRoundState.Running && round.StartedAt is { } start
        && !LiveSoloActiveClock.Paused(round.Pauses) && question.OpenedAt is null
        && question.Readiness == LiveSoloQuestionReadiness.Ready
        && LiveSoloActiveClock.Elapsed(start, now, round.Pauses) >= TimeSpan.FromSeconds(question.OpenOffsetSeconds)
        && LiveSoloActiveClock.Elapsed(start, now, round.Pauses) < TimeSpan.FromSeconds(round.LimitSeconds);

    public static void ApplyWinner(LiveSoloMatch match, LiveSoloRound round, LiveSoloResolutionDecision outcome, DateTimeOffset now)
    {
        if (round.State == LiveSoloRoundState.Won) return;
        if (outcome.State != LiveSoloResolution.Winner || outcome.WinnerTeamId is not Guid winner || outcome.WinningGameplayFactId is not Guid fact)
            throw new InvalidOperationException("A confirmed winning admission is required.");
        if (round.State is not (LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult))
            throw new InvalidOperationException("The Round is not active.");
        var side = match.Slots.Single(x => x.TeamId == winner).Side;
        round.State = LiveSoloRoundState.Won; round.WinnerTeamId = winner; round.WinningGameplayFactId = fact;
        round.LastResolvedSequence = outcome.ResolvedThrough; round.EndedAt = now; round.TimelineRevision++;
        if (side == LiveSoloSide.Left) match.LeftWins = checked(match.LeftWins + 1);
        else match.RightWins = checked(match.RightWins + 1);
        LiveSoloResults.Confirm(round, match, LiveSoloEndReason.CorrectAnswer);
        if (Math.Max(match.LeftWins, match.RightWins) >= match.RequiredWins)
        {
            match.State = LiveSoloMatchState.Completed; match.WinnerTeamId = winner; match.CompletedAt = now;
            LiveSoloResults.Confirm(match, LiveSoloEndReason.RequiredWinsReached, now);
        }
        else match.State = LiveSoloMatchState.Preparing;
    }

    public static bool TryVoid(LiveSoloRound round, LiveSoloResolutionDecision outcome, DateTimeOffset now)
    {
        if (round.StartedAt is null || round.State is not (LiveSoloRoundState.Running or LiveSoloRoundState.ConfirmingResult)
            || outcome.State != LiveSoloResolution.NoWinner
            || LiveSoloActiveClock.Elapsed(round.StartedAt.Value, now, round.Pauses) < TimeSpan.FromSeconds(round.LimitSeconds)) return false;
        round.State = LiveSoloRoundState.TimedOut; round.EndedAt = now; round.LastResolvedSequence = outcome.ResolvedThrough;
        round.TimelineRevision++; return true;
    }
}
