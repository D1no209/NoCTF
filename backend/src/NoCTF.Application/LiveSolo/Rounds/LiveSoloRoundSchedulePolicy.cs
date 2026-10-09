using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Rounds;

public static class LiveSoloRoundSchedulePolicy
{
    public const int PrewarmLeadSeconds = 30;
    public static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(2);

    public static bool FitsRuntimeQuota(int requiredConcurrentInstances, int configuredQuota) =>
        configuredQuota <= 0 || requiredConcurrentInstances <= configuredQuota;

    public static IReadOnlyList<Guid> PreparationCandidates(LiveSoloRound round, DateTimeOffset now)
    {
        if (round.State is LiveSoloRoundState.Preparing or LiveSoloRoundState.Countdown)
            return round.Questions.Where(x => x.Position == 0).Select(x => x.Id).ToArray();
        if (round.State != LiveSoloRoundState.Running || round.StartedAt is not { } start || LiveSoloActiveClock.Paused(round.Pauses)) return [];
        var elapsed = LiveSoloActiveClock.Elapsed(start, now, round.Pauses).TotalSeconds;
        if (elapsed >= round.LimitSeconds) return [];
        var next = round.Questions.Where(x => x.OpenedAt == null).OrderBy(x => x.Position).FirstOrDefault();
        return next is not null && elapsed >= Math.Max(0, next.OpenOffsetSeconds - PrewarmLeadSeconds) ? [next.Id] : [];
    }

    public static DateTimeOffset? NextWakeup(LiveSoloRound round, DateTimeOffset now)
    {
        if (LiveSoloActiveClock.Paused(round.Pauses)) return null;
        if (round.State == LiveSoloRoundState.Preparing) return round.Questions.Any(x => x.Position == 0 && x.Readiness != LiveSoloQuestionReadiness.Ready)
            ? now.Add(RecoveryInterval) : null;
        if (round.State == LiveSoloRoundState.Countdown && round.CountdownAt is { } countdown)
        {
            var remaining = round.CountdownSeconds - LiveSoloActiveClock.Elapsed(countdown, now, round.Pauses).TotalSeconds;
            if (remaining <= 0 && round.Questions.Any(x => x.Position == 0 && x.Readiness != LiveSoloQuestionReadiness.Ready))
                return now.Add(RecoveryInterval);
            return now.AddSeconds(Math.Max(0, remaining));
        }
        if (round.State == LiveSoloRoundState.Running && round.StartedAt is { } start)
        {
            var elapsed = LiveSoloActiveClock.Elapsed(start, now, round.Pauses).TotalSeconds;
            var remaining = Math.Max(0, round.LimitSeconds - elapsed);
            var next = round.Questions.Where(x => x.OpenedAt == null).OrderBy(x => x.Position).FirstOrDefault();
            if (next is not null)
            {
                var offset = next.Readiness == LiveSoloQuestionReadiness.Waiting ? Math.Max(0, next.OpenOffsetSeconds - PrewarmLeadSeconds) : next.OpenOffsetSeconds;
                var questionDelay = Math.Max(0, offset - elapsed);
                if (questionDelay == 0 && next.Readiness != LiveSoloQuestionReadiness.Ready) questionDelay = RecoveryInterval.TotalSeconds;
                remaining = Math.Min(remaining, questionDelay);
            }
            return now.AddSeconds(remaining);
        }
        return round.State == LiveSoloRoundState.ConfirmingResult ? now.Add(RecoveryInterval) : null;
    }
}
