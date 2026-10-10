using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Realtime;

public sealed class LiveSoloRealtimeAccess(NoCtfDbContext db) : ILiveSoloRealtimeAccess
{
    public async Task<IReadOnlySet<string>> EligibleAsync(IReadOnlyList<LiveSoloRealtimeAccessRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return new HashSet<string>();
        var userIds = requests.Select(x => x.UserId).Distinct().ToArray();
        var competitionIds = requests.Select(x => x.CompetitionId).Distinct().ToArray();
        var matchIds = requests.Select(x => x.MatchId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id) && x.AccountStatus == UserAccountStatus.Active).ToDictionaryAsync(x => x.Id, ct);
        var competitions = await db.Competitions.AsNoTracking().Include(x => x.Collaborators).Where(x => competitionIds.Contains(x.Id) && x.Mode == GameMode.LiveSolo
            && x.Status != CompetitionStatus.Draft && db.Set<LiveSoloCompetitionModeConfiguration>().Any(c => c.CompetitionId == x.Id && c.Enabled)).ToDictionaryAsync(x => x.Id, ct);
        var matches = await db.LiveSoloMatches.AsNoTracking().Include(x => x.Slots).Where(x => matchIds.Contains(x.Id) && competitionIds.Contains(x.CompetitionId)).ToDictionaryAsync(x => x.Id, ct);
        var teamIds = matches.Values.SelectMany(x => x.Slots).Where(x => x.TeamId != null).Select(x => x.TeamId!.Value).Distinct().ToArray();
        var teams = await db.Teams.AsNoTracking().Include(x => x.Members).Where(x => teamIds.Contains(x.Id) && x.RegistrationStatus == TeamRegistrationStatus.Approved && !x.IsBanned).ToDictionaryAsync(x => x.Id, ct);
        return requests.Where(request =>
        {
            if (!users.TryGetValue(request.UserId, out var user) || !competitions.TryGetValue(request.CompetitionId, out var competition)
                || !matches.TryGetValue(request.MatchId, out var match) || match.CompetitionId != competition.Id) return false;
            return request.Audience switch
            {
                LiveSoloRealtimeAudience.Staff => user.Role == UserRole.Administrator || competition.OwnerId == user.Id || competition.Collaborators.Any(x => x.UserId == user.Id),
                LiveSoloRealtimeAudience.Participant => match.Slots.Any(slot => slot.TeamId is Guid id && teams.TryGetValue(id, out var team)
                    && team.CompetitionId == competition.Id && team.Members.Any(member => member.UserId == user.Id)),
                _ => false
            };
        }).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
    }
}
