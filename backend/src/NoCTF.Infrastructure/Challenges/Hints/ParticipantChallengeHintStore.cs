using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Hints;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Hints;

public sealed class ParticipantChallengeHintStore(NoCtfDbContext db) : IParticipantChallengeHintStore
{
    public async Task<ParticipantChallengeHintAccess?> ReadAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var status = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == competitionId)
            .Select(item => (CompetitionStatus?)item.Status).SingleOrDefaultAsync(cancellationToken);
        if (status is not (CompetitionStatus.Running or CompetitionStatus.Paused or CompetitionStatus.Finished))
            return null;
        var challenge = await db.CompetitionChallenges.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == competitionChallengeId
                && item.CompetitionId == competitionId && item.IsPublished, cancellationToken);
        if (challenge is null)
            return null;
        var teamId = userId == Guid.Empty ? null : await db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.MemberIds.Contains(userId)
                && team.RegistrationStatus == TeamRegistrationStatus.Approved && !team.IsBanned)
            .Select(team => (Guid?)team.Id).SingleOrDefaultAsync(cancellationToken);
        var unlocked = teamId is null ? [] : await db.GameplayFacts.AsNoTracking()
            .Where(fact => fact.CompetitionId == competitionId
                && fact.CompetitionChallengeId == competitionChallengeId && fact.TeamId == teamId
                && fact.Kind == GameplayFactKind.HintUnlock && fact.ReferenceKind == GameplayFactReferenceKind.Hint
                && fact.ReferenceId != null && fact.State == GameplayFactState.Completed
                && fact.Result == GameplayFactResult.Unlocked)
            .Select(fact => fact.ReferenceId!.Value).ToListAsync(cancellationToken);
        var canUnlock = teamId is not null && status == CompetitionStatus.Running;
        return new(
            challenge.Hints.Select(hint => new ChallengeHintView(
                hint.Id, challenge.Id, hint.Content, hint.Cost, hint.PublishedAt,
                hint.HiddenAt, challenge.UpdatedAt, challenge.UpdatedAt)).ToArray(),
            unlocked.ToHashSet(), canUnlock);
    }
}
