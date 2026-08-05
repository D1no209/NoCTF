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
                candidate.Status,
                candidate.OwnerId,
                candidate.ManagerIds,
                candidate.JudgeIds,
                candidate.ObserverIds,
                candidate.LeaderboardVisibility,
                candidate.LeaderboardVisibilityStartsAt,
                candidate.LeaderboardVisibilityRevision,
                candidate.LeaderboardRevision
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return null;

        var identity = userId == Guid.Empty
            ? null
            : await db.Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new { user.Kind, user.Role })
                .SingleOrDefaultAsync(ct);
        var isCollaborator = identity is not null
            && (identity.Role == UserRole.Administrator
                || competition.OwnerId == userId
                || competition.ManagerIds.Contains(userId)
                || competition.JudgeIds.Contains(userId)
                || competition.ObserverIds.Contains(userId));
        if (competition.Status == CompetitionStatus.Draft && !isCollaborator)
            return null;

        var visibility = CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            competition.Status,
            competition.LeaderboardVisibility,
            competition.LeaderboardVisibilityStartsAt,
            now);
        var scope = DataScope(identity?.Kind, isCollaborator, visibility);
        return new(
            visibility,
            scope,
            competition.LeaderboardVisibilityRevision,
            competition.LeaderboardRevision);
    }

    private static LeaderboardDataScope DataScope(
        UserKind? userKind,
        bool isCollaborator,
        CompetitionLeaderboardVisibility visibility)
    {
        if (isCollaborator && userKind != UserKind.Bot)
            return LeaderboardDataScope.Live;
        if (isCollaborator && userKind == UserKind.Bot)
            return visibility == CompetitionLeaderboardVisibility.Blackout
                ? LeaderboardDataScope.Hidden
                : LeaderboardDataScope.Live;

        return visibility switch
        {
            CompetitionLeaderboardVisibility.Normal => LeaderboardDataScope.Live,
            CompetitionLeaderboardVisibility.Frozen => LeaderboardDataScope.Frozen,
            CompetitionLeaderboardVisibility.Blackout => LeaderboardDataScope.Hidden,
            _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
        };
    }
}
