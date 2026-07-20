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
        var statuses = await db.Database.SqlQuery<int>(
                $"""SELECT "Status" AS "Value" FROM competitions WHERE "Id" = {competitionId} AND NOT "Deletion_IsDeleted" FOR UPDATE""")
            .ToListAsync(cancellationToken);
        return statuses.Count == 1 ? (CompetitionStatus)statuses[0] : null;
    }
}
