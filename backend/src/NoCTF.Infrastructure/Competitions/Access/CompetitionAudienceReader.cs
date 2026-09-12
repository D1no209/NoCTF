using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Access;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Competitions.Management;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Access;

public sealed class CompetitionAudienceReader(
    NoCtfDbContext db,
    CompetitionReadModelCache readModels) : ICompetitionAudienceReader
{
    public Task<CompetitionAccessMode?> GetAccessModeAsync(
        Guid competitionId,
        CancellationToken cancellationToken) =>
        readModels.GetAccessModeAsync(
            competitionId,
            token => db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == competitionId)
                .Select(competition => (CompetitionAccessMode?)competition.AccessMode)
                .SingleOrDefaultAsync(token),
            cancellationToken);
}
