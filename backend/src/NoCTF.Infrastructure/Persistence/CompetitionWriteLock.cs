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
        if (!db.Database.IsRelational())
        {
            return await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == competitionId
                    && competition.DeletedAt == null)
                .Select(competition => (CompetitionStatus?)competition.Status)
                .SingleOrDefaultAsync(cancellationToken);
        }
        await AcquireTransactionLockAsync(db, competitionId, cancellationToken);
        var statuses = await db.Database.SqlQuery<short>(
                $"""SELECT status AS "Value" FROM competitions WHERE id = {competitionId} AND deleted_at IS NULL""")
            .ToListAsync(cancellationToken);
        return statuses.Count == 1 ? (CompetitionStatus)statuses[0] : null;
    }

    public static Task AcquireTransactionLockAsync(
        NoCtfDbContext db,
        Guid competitionId,
        CancellationToken cancellationToken) =>
        !db.Database.IsRelational()
            ? Task.CompletedTask
            : db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'c' + competitionId.ToString("N")}, 0))",
            cancellationToken);
}
