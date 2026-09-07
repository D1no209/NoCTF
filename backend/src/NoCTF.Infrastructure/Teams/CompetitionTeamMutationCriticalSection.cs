using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams;

internal static class CompetitionTeamMutationCriticalSection
{
    public static Task<Competition?> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return db.Competitions.SingleOrDefaultAsync(
                competition => competition.Id == competitionId,
                cancellationToken);
        }

        return db.Competitions
            .FromSqlInterpolated($"""
                SELECT *
                FROM competitions
                WHERE id = {competitionId}
                    AND deleted_at IS NULL
                FOR NO KEY UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
