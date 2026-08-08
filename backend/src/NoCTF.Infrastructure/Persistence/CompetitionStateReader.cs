using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence;

internal static class CompetitionStateReader
{
    public static async Task<CompetitionStatus?> ReadAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        return await db.Competitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(candidate => candidate.Id == competitionId && candidate.DeletedAt == null)
            .Select(candidate => (CompetitionStatus?)candidate.Status)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
