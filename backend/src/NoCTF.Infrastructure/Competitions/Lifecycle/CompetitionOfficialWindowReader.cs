using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

internal static class CompetitionOfficialWindowReader
{
    public static async Task<CompetitionOfficialWindow> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        DateTimeOffset startAt,
        DateTimeOffset scheduledEndAt,
        CancellationToken ct)
    {
        var lifecycleEvents = await db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .Select(@event => new { @event.OccurredAt, @event.CompetitionStatus })
            .ToArrayAsync(ct);
        var finishedAt = lifecycleEvents
            .Where(@event => @event.CompetitionStatus == CompetitionStatus.Finished)
            .Select(@event => (DateTimeOffset?)@event.OccurredAt)
            .FirstOrDefault();
        return CompetitionOfficialWindow.Resolve(startAt, scheduledEndAt, finishedAt);
    }
}
