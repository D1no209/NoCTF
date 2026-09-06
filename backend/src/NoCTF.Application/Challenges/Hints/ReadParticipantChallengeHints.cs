namespace NoCTF.Application.Challenges.Hints;

public sealed record ParticipantChallengeHintView(
    Guid Id,
    long Cost,
    DateTimeOffset PublishedAt,
    string? Content,
    bool IsUnlocked,
    bool CanUnlock);

public sealed record ParticipantChallengeHintAccess(
    IReadOnlyList<ChallengeHintView> Hints,
    IReadOnlySet<Guid> UnlockedHintIds,
    bool CanUnlock);

public interface IParticipantChallengeHintStore
{
    Task<ParticipantChallengeHintAccess?> ReadAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed class ReadParticipantChallengeHints(IParticipantChallengeHintStore store)
{
    public async Task<IReadOnlyList<ParticipantChallengeHintView>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var access = await store.ReadAsync(competitionId, competitionChallengeId, userId, cancellationToken);
        if (access is null)
            return [];
        return access.Hints
            .Where(hint => hint.DeletedAt is null && hint.PublishedAt is { } published && published <= now)
            .OrderBy(hint => hint.PublishedAt)
            .ThenBy(hint => hint.Id)
            .Select(hint =>
            {
                var unlocked = hint.Cost == 0 || access.UnlockedHintIds.Contains(hint.Id);
                return new ParticipantChallengeHintView(
                    hint.Id, hint.Cost, hint.PublishedAt!.Value,
                    unlocked ? hint.Content : null, unlocked, !unlocked && access.CanUnlock);
            }).ToArray();
    }
}
