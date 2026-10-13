using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal sealed record AwdCompetitionDeadline(AwdRunningClock Clock, IReadOnlyList<AwdRound> Rounds,
    TimeSpan? ActiveTimeLimit, DateTimeOffset EndsAt, DateTimeOffset AcceptanceEndsAt, bool EndIsProvisional)
{
    public static async Task<AwdCompetitionDeadline> ReadAsync(NoCtfDbContext db, Guid competitionId,
        DateTimeOffset startsAt, DateTimeOffset scheduledEndAt, AwdCompetitionModeConfiguration configuration,
        DateTimeOffset observedAt, CancellationToken ct)
    {
        var clock = await AwdRunningClock.ReadAsync(db, competitionId, startsAt, observedAt, ct);
        var rounds = await db.AwdRounds.AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .OrderBy(x => x.Number).ToArrayAsync(ct);
        var finishedAt = await db.CompetitionEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId
                && x.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                && x.CompetitionStatus == CompetitionStatus.Finished && x.OccurredAt <= observedAt)
            .Select(x => (DateTimeOffset?)x.OccurredAt).MaxAsync(ct);
        // EndAt is the scheduled running-time budget, extended only by explicit pauses.
        // Gaps are added by the round timeline, so they cannot consume that budget twice.
        var budget = clock.ElapsedAt(scheduledEndAt);
        // An open pause freezes elapsed time; it does not consume the unspent running budget.
        var pausedAt = finishedAt is null ? clock.StoppedAt : null;
        if (pausedAt is { } stopped && scheduledEndAt > stopped)
            budget += scheduledEndAt - stopped;
        var limit = configuration.AwdOptions.TotalRounds is null ? budget : (TimeSpan?)null;
        var endPosition = AwdRoundTimeline.EndPosition(rounds,
            TimeSpan.FromSeconds(configuration.HardeningDurationSeconds),
            TimeSpan.FromSeconds(configuration.RoundDurationSeconds), configuration.AwdOptions.RoundGapSeconds,
            configuration.AwdOptions.TotalRounds, budget);
        var end = finishedAt ?? (clock.HasIntervals
            ? clock.At(endPosition, intervalStart: false, clampClosed: true) : scheduledEndAt);
        var last = rounds.LastOrDefault(round => round.StartsAt <= end);
        // Once the terminal round exists (or the match has stopped), future defaults
        // cannot reopen intake. A round without enabled questions still owns this policy.
        var tail = last is not null && (finishedAt is not null || observedAt >= end || end <= last.EndsAt)
            ? last.SubmissionTailSeconds
            : finishedAt is not null ? 0 : configuration.AwdOptions.SubmissionTailSeconds;
        var provisional = pausedAt is not null && clock.ElapsedAt(observedAt) < endPosition;
        return new(clock, rounds, limit, end, end.AddSeconds(tail), provisional);
    }
}
