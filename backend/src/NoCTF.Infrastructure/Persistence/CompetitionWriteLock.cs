using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence;

internal static class CompetitionWriteLock
{
    public static async Task<CompetitionStatus?> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId
                && candidate.DeletedAt == null, cancellationToken);
        if (competition is null)
            return null;
        competition.ConcurrencyVersion = checked(competition.ConcurrencyVersion + 1);
        return competition.Status;
    }

    public static async Task AcquireTransactionLockAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == competitionId, cancellationToken);
        if (competition is not null)
            competition.ConcurrencyVersion = checked(competition.ConcurrencyVersion + 1);
    }
}
