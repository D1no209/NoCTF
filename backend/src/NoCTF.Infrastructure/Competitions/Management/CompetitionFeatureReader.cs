using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Management;

public sealed class CompetitionFeatureReader(NoCtfDbContext db) : ICompetitionFeatureReader
{
    public async Task<CompetitionFeatures> ReadAsync(Guid competitionId, CancellationToken ct)
    {
        var progression = await db.CompetitionProgressions.AsNoTracking()
            .Where(item => item.CompetitionId == competitionId)
            .Select(item => new { item.Enabled, item.ShowPlayerMap }).SingleOrDefaultAsync(ct);
        var theory = await db.CompetitionTheoryActivities.AsNoTracking()
            .Where(link => link.CompetitionId == competitionId && link.Enabled)
            .Join(db.TheoryActivities.AsNoTracking(), link => link.ActivityId, activity => activity.Id,
                (_, activity) => activity)
            .AnyAsync(activity => activity.State != TheoryActivityState.Draft
                && activity.State != TheoryActivityState.Voided, ct);
        return new(progression?.Enabled ?? false, progression?.ShowPlayerMap ?? false, theory);
    }
}
