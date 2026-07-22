using Microsoft.EntityFrameworkCore;
using NoCTF.Application.SystemProducers;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfRunningCompetitionStore(NoCtfDbContext db) : IRunningCompetitionStore
{
    public async Task<IReadOnlyList<Guid>> ListRunningAsync(GameMode mode, CancellationToken cancellationToken) =>
        await db.Competitions.AsNoTracking()
            .Where(competition => competition.Mode == mode && competition.Status == CompetitionStatus.Running)
            .OrderBy(competition => competition.Id)
            .Select(competition => competition.Id)
            .ToListAsync(cancellationToken);
}
