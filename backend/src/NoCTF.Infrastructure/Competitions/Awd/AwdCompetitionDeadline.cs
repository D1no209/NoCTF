using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Awd;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Awd.Scheduling;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal sealed record AwdCompetitionDeadline(AwdRunningClock Clock, IReadOnlyList<AwdRound> Rounds,
    TimeSpan? ActiveTimeLimit, DateTimeOffset EndsAt, DateTimeOffset AcceptanceEndsAt)
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
        var limit = configuration.AwdOptions.TotalRounds is null ? budget : (TimeSpan?)null;
        var endPosition = AwdRoundTimeline.EndPosition(rounds,
            TimeSpan.FromSeconds(configuration.HardeningDurationSeconds),
            TimeSpan.FromSeconds(configuration.RoundDurationSeconds), configuration.AwdOptions.RoundGapSeconds,
            configuration.AwdOptions.TotalRounds, budget);
        var end = finishedAt ?? (clock.HasIntervals
            ? clock.At(endPosition, intervalStart: false, clampClosed: true) : scheduledEndAt);
        return new(clock, rounds, limit, end, end.AddSeconds(configuration.AwdOptions.SubmissionTailSeconds));
    }
}
