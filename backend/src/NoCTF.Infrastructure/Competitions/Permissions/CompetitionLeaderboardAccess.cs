using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionLeaderboardAccess(NoCtfDbContext db)
    : ICompetitionLeaderboardAccess
{
    public Task<bool> CanReadPrivateAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId && user.Kind == UserKind.Bot)
            .AnyAsync(user => db.Competitions.AsNoTracking().Any(competition =>
                competition.Id == competitionId
                && competition.DeletedAt == null
                && competition.ObserverIds.Contains(user.Id)), ct);
}
