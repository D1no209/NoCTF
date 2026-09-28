using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Lifecycle;

internal static class CompetitionEffectiveRuntimeReader
{
    public static async Task<CompetitionEffectiveRuntime> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        DateTimeOffset competitionStartAt,
        DateTimeOffset competitionEndAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stored = await db.CompetitionEvents.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId
                && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
            .OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.OccurredAt,
                From = item.PreviousCompetitionStatus,
                To = item.CompetitionStatus
            })
            .ToArrayAsync(cancellationToken);
        var transitions = stored.Select(item => new CompetitionLifecycleMoment(
            item.Id,
            item.OccurredAt,
            item.From ?? throw new InvalidOperationException(
                $"Competition lifecycle event {item.Id} has no previous status."),
            item.To ?? throw new InvalidOperationException(
                $"Competition lifecycle event {item.Id} has no current status.")));
        return CompetitionEffectiveRuntimePolicy.Calculate(
            competitionStartAt,
            competitionEndAt,
            now,
            transitions);
    }
}
