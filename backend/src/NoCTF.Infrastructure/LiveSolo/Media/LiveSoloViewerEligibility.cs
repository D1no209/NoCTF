using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Media;

internal static class LiveSoloViewerEligibility
{
    internal static async Task<bool> WithinCapacityAsync(NoCtfDbContext db, Guid competitionId, Guid leaseId, DateTimeOffset now, CancellationToken ct)
    {
        var maximum = await db.Set<LiveSoloCompetitionModeConfiguration>().Where(x => x.CompetitionId == competitionId).Select(x => x.MaximumViewers).SingleAsync(ct);
        return await db.Set<LiveSoloViewerLease>().Where(x => x.CompetitionId == competitionId && x.ExpiresAt > now)
            .OrderBy(x => x.Id).Take(maximum).AnyAsync(x => x.Id == leaseId, ct);
    }
    internal static async Task<bool> AllowedAsync(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer,
        Guid competitionId, Guid matchId, Guid actorId, CancellationToken ct)
    {
        if (actorId != Guid.Empty && !await db.Users.AnyAsync(x => x.Id == actorId && x.AccountStatus == UserAccountStatus.Active, ct)) return false;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId && x.Mode == GameMode.LiveSolo, ct);
        if (competition is null || competition.Status is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished)
            || !await db.Set<LiveSoloCompetitionModeConfiguration>().AnyAsync(x => x.CompetitionId == competitionId && x.Enabled, ct)
            || !await db.LiveSoloMatches.AnyAsync(x => x.Id == matchId && x.CompetitionId == competitionId && x.StartedAt != null, ct)) return false;
        return competition.AccessMode == CompetitionAccessMode.Public
            || actorId != Guid.Empty && await authorizer.CanObserveAsync(actorId, competitionId, ct);
    }
}
