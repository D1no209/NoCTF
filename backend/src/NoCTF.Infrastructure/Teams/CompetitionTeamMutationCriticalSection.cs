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
        => db.Competitions.Include(competition => competition.Tracks)
            .SingleOrDefaultAsync(
            competition => competition.Id == competitionId,
            cancellationToken);
}
