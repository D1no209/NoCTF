using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed class LiveSoloPlayerPolicyReader(NoCtfDbContext db, ICompetitionModerationAuthorizer authorizer) : ILiveSoloPlayerPolicyReader
{
    public async Task<LiveSoloPlayerPolicy?> ReadAsync(Guid competitionId, Guid actorId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actorId && x.AccountStatus == UserAccountStatus.Active, ct)) return null;
        var competition = await db.Competitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == competitionId && x.Mode == GameMode.LiveSolo, ct);
        if (competition is null) return null;
        if (!await authorizer.CanObserveAsync(actorId, competitionId, ct)
            && (competition.Status == CompetitionStatus.Draft
                || !await db.Teams.AnyAsync(x => x.CompetitionId == competitionId && x.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !x.IsBanned && x.Members.Any(m => m.UserId == actorId), ct))) return null;
        return await db.Set<LiveSoloCompetitionModeConfiguration>().AsNoTracking().Where(x => x.CompetitionId == competitionId)
            .Select(x => new LiveSoloPlayerPolicy(x.Enabled, x.RequiredWins, x.MaximumRosterMembers, x.PublicDelaySeconds,
                x.ParticipantsMayViewOpponents, x.RecordingEnabled)).SingleOrDefaultAsync(ct);
    }
}
