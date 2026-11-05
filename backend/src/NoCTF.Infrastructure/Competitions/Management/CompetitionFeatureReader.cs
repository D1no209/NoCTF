using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Management;

public sealed class CompetitionFeatureReader(NoCtfDbContext db) : ICompetitionFeatureReader
{
    public async Task<CompetitionFeatures> ReadAsync(Guid competitionId, CancellationToken ct)
    {
        var progression = await db.CompetitionProgressions.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .Select(item => new { item.Enabled, item.ShowPlayerMap }).SingleOrDefaultAsync(ct);
        return new(progression?.Enabled ?? false, progression?.ShowPlayerMap ?? false);
    }
}
