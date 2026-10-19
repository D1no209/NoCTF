using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Visibility;

public sealed class CompetitionVisibilityAccess(NoCtfDbContext db,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null)
    : ICompetitionVisibilityAccess
{
    public async Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (userId == Guid.Empty && readModels is not null)
        {
            // Anonymous decisions depend only on the competition row, whose writers invalidate this read model.
            var anonymous = await readModels.GetAnonymousAccessAsync(competitionId, token => db.Competitions.AsNoTracking()
                .Where(candidate => candidate.Id == competitionId)
                .Select(candidate => new NoCTF.Infrastructure.Competitions.Management.CompetitionAnonymousAccess(
                    candidate.Mode, candidate.Status, candidate.AccessMode, candidate.TracksEnabled,
                    candidate.FrozenStartAt, candidate.HiddenStartAt))
                .SingleOrDefaultAsync(token), ct);
            if (anonymous is null
                || anonymous.Status == CompetitionStatus.Draft
                || anonymous.AccessMode == CompetitionAccessMode.StaffOnly)
                return null;
            var anonymousVisibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
                anonymous.FrozenStartAt, anonymous.HiddenStartAt, now);
            return new(anonymous.Mode, anonymous.Status, anonymousVisibility,
                DataScope(false, anonymousVisibility), anonymous.TracksEnabled);
        }

        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId)
            .Select(candidate => new
            {
                candidate.Mode,
                candidate.Status,
                candidate.AccessMode,
                candidate.TracksEnabled,
                candidate.OwnerId,
                IsCollaborator = candidate.Collaborators.Any(collaborator =>
                    collaborator.UserId == userId),
                IsAdministrator = db.Users.Any(user => user.Id == userId && user.Role == UserRole.Administrator),
                candidate.FrozenStartAt,
                candidate.HiddenStartAt
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;

        var isCollaborator = userId != Guid.Empty
            && (competition.IsAdministrator
                || competition.OwnerId == userId
                || competition.IsCollaborator);
        if ((competition.Status == CompetitionStatus.Draft
                || competition.AccessMode == CompetitionAccessMode.StaffOnly)
            && !isCollaborator)
            return null;

        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.FrozenStartAt,
            competition.HiddenStartAt,
            now);
        var scope = DataScope(isCollaborator, visibility);
        return new(
            competition.Mode,
            competition.Status,
            visibility,
            scope,
            competition.TracksEnabled);
    }

    private static LeaderboardDataScope DataScope(
        bool isCollaborator,
        CompetitionLeaderboardVisibility visibility)
    {
        if (isCollaborator)
            return LeaderboardDataScope.Live;

        return visibility switch
        {
            CompetitionLeaderboardVisibility.Normal => LeaderboardDataScope.Live,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };
    }
}
