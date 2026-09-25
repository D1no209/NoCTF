using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Hints;

public sealed class ParticipantChallengeHintStore(NoCtfDbContext db) : IParticipantChallengeHintStore
{
    public async Task<ParticipantChallengeHintAccess?> ReadAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        CompetitionStatus competitionStatus,
        Guid? teamId,
        CancellationToken cancellationToken)
    {
        if (competitionStatus is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished))
            return null;
        var challenge = await db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes()
            .Where(item => item.Id == competitionChallengeId
                && item.CompetitionId == competitionId && item.IsPublished)
            .Select(item => new
            {
                item.Id,
                item.UpdatedAt,
                Hints = item.Hints.Select(hint => new ChallengeHintView(
                    hint.Id, item.Id, hint.Content, hint.Cost, hint.PublishedAt,
                    hint.HiddenAt, item.UpdatedAt, item.UpdatedAt)).ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (challenge is null)
            return null;
        var unlocked = teamId is null ? [] : await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.CompetitionChallengeId == competitionChallengeId && fact.TeamId == teamId
                && fact.Kind == GameplayFactKind.HintUnlock && fact.ReferenceKind == GameplayFactReferenceKind.Hint
                && fact.ReferenceId != null && fact.State == GameplayFactState.Completed
                && fact.Result == GameplayFactResult.Unlocked)
            .Select(fact => fact.ReferenceId!.Value).ToListAsync(cancellationToken);
        var canUnlock = teamId is not null && competitionStatus == CompetitionStatus.Running;
        return new(
            challenge.Hints,
            unlocked.ToHashSet(), canUnlock);
    }
}
