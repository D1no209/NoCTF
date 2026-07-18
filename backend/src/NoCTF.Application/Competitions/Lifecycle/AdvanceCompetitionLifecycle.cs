using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleSnapshot(
    Guid CompetitionId,
    CompetitionStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public interface ICompetitionLifecycleStore
{
    Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TryTransitionAsync(Guid competitionId, CompetitionStatus from, CompetitionStatus to, CancellationToken cancellationToken);
}

/// <summary>Advances published and running competitions using wall-clock deadlines without extending pauses.</summary>
public sealed class AdvanceCompetitionLifecycle(ICompetitionLifecycleStore store)
{
    public async Task ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        foreach (var competition in await store.GetDueAsync(now, cancellationToken))
        {
            if (competition.Status == CompetitionStatus.Published && now >= competition.StartTime)
                await store.TryTransitionAsync(competition.CompetitionId, competition.Status, CompetitionStatus.Running, cancellationToken);

            if (competition.Status is CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Paused
                && now >= competition.EndTime)
                await store.TryTransitionAsync(competition.CompetitionId, competition.Status, CompetitionStatus.Finished, cancellationToken);
        }
    }
}
