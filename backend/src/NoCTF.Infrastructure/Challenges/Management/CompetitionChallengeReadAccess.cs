using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class CompetitionChallengeReadAccess(NoCtfDbContext db)
    : ICompetitionChallengeReadAccess
{
    public async Task<CompetitionChallengeReadDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return null;

        var identity = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new { user.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
            return null;

        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => new
            {
                item.Mode,
                item.Status,
                item.AccessMode,
                item.TracksEnabled,
                item.OwnerId,
                item.DeletedAt,
                item.FrozenStartAt,
                item.HiddenStartAt,
                IsCollaborator = item.Collaborators.Any(collaborator => collaborator.UserId == userId)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return null;

        var isStaff = identity.Role == UserRole.Administrator
            || competition.OwnerId == userId
            || competition.IsCollaborator;
        if ((competition.Status == CompetitionStatus.Draft
                || competition.AccessMode == CompetitionAccessMode.StaffOnly)
            && !isStaff)
            return null;

        var team = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.Members.Any(member => member.UserId == userId))
            .Select(team => new { team.Id, team.DeletedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity.Role != UserRole.Administrator
            && !(competition.DeletedAt is null && isStaff)
            && (team is null || team.DeletedAt is not null))
            return null;

        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt, competition.HiddenStartAt, now);
        var scope = isStaff
            ? LeaderboardDataScope.Live
            : visibility switch
            {
                CompetitionLeaderboardVisibility.Normal => LeaderboardDataScope.Live,
                CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
                CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
                _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
            };
        return new(new(
            competition.Mode,
            competition.Status,
            visibility,
            scope,
            competition.TracksEnabled), team?.Id);
    }
}
