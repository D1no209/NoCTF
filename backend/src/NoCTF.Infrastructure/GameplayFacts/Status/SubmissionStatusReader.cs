using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Status;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Infrastructure.GameplayFacts.Status;

public sealed class GameplayFactStatusReader(NoCtfDbContext db) : IGameplayFactStatusReader
{
    public async Task<GameplayFactStatusView?> FindAsync(
        Guid competitionId,
        Guid gameplayFactId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var isStaff = await db.Competitions.AsNoTracking().AnyAsync(
                competition => competition.Id == competitionId
                    && (competition.OwnerId == userId
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer && collaborator.UserId == userId)),
                cancellationToken);
        var teamId = await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.Members.Any(member => member.UserId == userId))
            .Select(team => (Guid?)team.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!isStaff && teamId is null)
            return null;
        return await db.GameplayFacts.AsNoTracking()
            .Where(submission =>
                submission.Id == gameplayFactId
                && submission.CompetitionId == competitionId
                && (isStaff
                    || submission.TeamId == teamId
                    && (submission.Kind == GameplayFactKind.FlagAttempt
                        || submission.Kind == GameplayFactKind.BreakAttempt
                        || submission.Kind == GameplayFactKind.FixAttempt
                        || submission.Kind == GameplayFactKind.HintUnlock)))
            .Select(fact => new GameplayFactStatusView(
                fact.Id,
                fact.CompetitionId,
                fact.TeamId,
                fact.CompetitionChallengeId,
                fact.Kind,
                fact.State,
                GameplayFactResultDisclosure.PlayerResult(fact.Result, fact.FailureCode),
                GameplayFactResultDisclosure.PlayerFailureCode(fact.FailureCode),
                fact.OccurredAt,
                fact.UpdatedAt, fact.TimeEligibility))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
