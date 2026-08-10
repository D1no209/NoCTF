using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.GameplayFacts.Administration;

public sealed class AdminGameplayFactStatusReader(NoCtfDbContext db) : IAdminGameplayFactStatusReader
{
    public Task<AdminGameplayFactStatusView?> FindAsync(
        Guid competitionId,
        Guid gameplayFactId,
        CancellationToken cancellationToken) =>
        db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.CompetitionId == competitionId && submission.Id == gameplayFactId)
            .Select(fact => new AdminGameplayFactStatusView(
                fact.Id,
                fact.CompetitionId,
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.ActorUserId,
                fact.Kind,
                fact.State,
                fact.Result,
                fact.FailureCode,
                fact.OccurredAt,
                fact.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);
}
