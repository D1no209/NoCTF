using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Standings;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams.Standings;

public sealed class MyTeamStandingIdentityReader(NoCtfDbContext db) : IMyTeamStandingIdentityReader
{
    public Task<MyTeamStandingIdentity?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct) =>
        db.Teams.AsNoTracking().IgnoreAutoIncludes()
            .Where(team => team.CompetitionId == competitionId && team.DeletedAt == null
                && team.Members.Any(member => member.UserId == actorId))
            .Select(team => new MyTeamStandingIdentity(team.Id, team.IsBanned, team.RegistrationStatus, team.TrackKey))
            .SingleOrDefaultAsync(ct);
}
