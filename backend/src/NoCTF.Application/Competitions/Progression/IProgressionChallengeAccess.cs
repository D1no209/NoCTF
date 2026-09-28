namespace NoCTF.Application.Competitions.Progression;

public sealed record ProgressionChallengeStatus(
    bool Active, int PrerequisitesSatisfied, int PrerequisitesTotal);

public interface IProgressionChallengeAccess
{
    Task<IReadOnlyDictionary<Guid, ProgressionChallengeStatus>> ReadStatusesAsync(
        Guid competitionId, Guid? teamId, CancellationToken ct);

    Task<bool> IsActiveAsync(
        Guid competitionId, Guid competitionChallengeId, Guid? teamId,
        CancellationToken ct);
}
