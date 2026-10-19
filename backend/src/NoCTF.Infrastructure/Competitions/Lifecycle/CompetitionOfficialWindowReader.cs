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
        var finishedAt = await FinishedAt(db, competitionId).FirstOrDefaultAsync(ct);
        return CompetitionOfficialWindow.Resolve(startAt, scheduledEndAt, finishedAt);
    }

    /// <summary>The first transition to Finished; composable into a larger projection.</summary>
    public static IQueryable<DateTimeOffset?> FinishedAt(NoCtfDbContext db, Guid competitionId) =>
        db.CompetitionEvents.AsNoTracking()
            .Where(@event => @event.CompetitionId == competitionId
                && @event.Kind == CompetitionEventKind.CompetitionLifecycleChanged
                && @event.CompetitionStatus == CompetitionStatus.Finished)
            .OrderBy(@event => @event.OccurredAt)
            .ThenBy(@event => @event.Id)
            .Select(@event => (DateTimeOffset?)@event.OccurredAt);
}
