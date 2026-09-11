namespace NoCTF.Domain.Competitions;

/// <summary>Defines the half-open wall-clock window whose participant facts affect official results.</summary>
public readonly record struct CompetitionOfficialWindow(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt)
{
    public bool Contains(DateTimeOffset occurredAt) =>
        occurredAt >= StartAt && occurredAt < EndAt;

    public static CompetitionOfficialWindow Resolve(
        DateTimeOffset startAt,
        DateTimeOffset scheduledEndAt,
        DateTimeOffset? finishedAt = null) =>
        new(
            startAt,
            finishedAt is DateTimeOffset terminalAt && terminalAt < scheduledEndAt
                ? terminalAt
                : scheduledEndAt);

    public static CompetitionOfficialWindow Resolve(
        DateTimeOffset startAt,
        DateTimeOffset scheduledEndAt,
        IEnumerable<CompetitionLifecycleTransition> lifecycle) =>
        Resolve(
            startAt,
            scheduledEndAt,
            lifecycle
                .Where(transition => transition.To == CompetitionStatus.Finished)
                .OrderBy(transition => transition.OccurredAt)
                .Select(transition => (DateTimeOffset?)transition.OccurredAt)
                .FirstOrDefault());
}
