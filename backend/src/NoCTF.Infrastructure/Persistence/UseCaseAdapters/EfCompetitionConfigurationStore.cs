using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Configuration;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionConfigurationStore(NoCtfDbContext db) : ICompetitionConfigurationStore
{
    public Task<CompetitionConfigurationView?> FindAsync(Guid competitionId, CancellationToken ct) =>
        (from config in db.CompetitionConfigurations.AsNoTracking()
         join competition in db.Competitions.AsNoTracking() on config.CompetitionId equals competition.Id
         where config.CompetitionId == competitionId && !competition.Deletion.IsDeleted
         select new CompetitionConfigurationView(config.CompetitionId, config.Mode, config.Json, config.Revision,
             competition.Status, config.UpdatedAt)).SingleOrDefaultAsync(ct);

    public async Task<CompetitionConfigurationView?> TryUpdateAsync(Guid competitionId, int expectedRevision, string json, DateTimeOffset now, CancellationToken ct)
    {
        var changed = await db.CompetitionConfigurations
            .Where(x => x.CompetitionId == competitionId && x.Revision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Json, json)
                .SetProperty(x => x.Revision, expectedRevision + 1)
                .SetProperty(x => x.UpdatedAt, now), ct);
        return changed == 1 ? await FindAsync(competitionId, ct) : null;
    }
}
