using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Visibility;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Visibility;

public sealed class CompetitionVisibilityAccess(NoCtfDbContext db)
    : ICompetitionVisibilityAccess
{
    public async Task<CompetitionVisibilityAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
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
                candidate.FrozenStartAt,
                candidate.HiddenStartAt
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;

        var identity = userId == Guid.Empty
            ? null
            : await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.Role })
                .SingleOrDefaultAsync(ct);
        var isCollaborator = identity is not null
            && (identity.Role == UserRole.Administrator
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
