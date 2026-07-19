using NoCTF.Domain.Competitions;
using NoCTF.Application.Common;
using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleSnapshot(
    Guid CompetitionId,
    CompetitionStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public interface ICompetitionLifecycleStore
{
    Task<CompetitionStatus?> GetStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompetitionLifecycleSnapshot>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> TryTransitionAsync(Guid competitionId, CompetitionStatus from, CompetitionStatus to, CancellationToken cancellationToken);
    Task<bool> TryTransitionWithAuditAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        Guid? actorId,
        string? reason,
        bool automatic,
        CancellationToken cancellationToken) =>
        TryTransitionAsync(competitionId, from, to, cancellationToken);
}

/// <summary>Advances published and running competitions using wall-clock deadlines without extending pauses.</summary>
public sealed class AdvanceCompetitionLifecycle(ICompetitionLifecycleStore store)
{
    public async Task<IReadOnlyList<CompetitionLifecycleTransition>> ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var transitions = new List<CompetitionLifecycleTransition>();
        foreach (var competition in await store.GetDueAsync(now, cancellationToken))
        {
            if (competition.Status == CompetitionStatus.Published && now >= competition.StartTime)
            {
                if (await store.TryTransitionWithAuditAsync(competition.CompetitionId, competition.Status, CompetitionStatus.Running, null, "start_time_reached", true, cancellationToken))
                    transitions.Add(new(competition.CompetitionId, competition.Status, CompetitionStatus.Running));
            }

            if (competition.Status is CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Paused
                && now >= competition.EndTime)
            {
                if (await store.TryTransitionWithAuditAsync(competition.CompetitionId, competition.Status, CompetitionStatus.Finished, null, "end_time_reached", true, cancellationToken))
                    transitions.Add(new(competition.CompetitionId, competition.Status, CompetitionStatus.Finished));
            }
        }
        return transitions;
    }
}

public sealed class TransitionCompetitionLifecycle(
    ICompetitionLifecycleStore store,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<OperationResult> ExecuteAsync(
        Guid competitionId,
        CompetitionStatus target,
        Guid? actorId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var current = await store.GetStatusAsync(competitionId, cancellationToken);
        if (current is null)
            return OperationResult.Failure("competition_not_found", "Competition was not found.");
        var validation = CompetitionLifecyclePolicy.ValidateTransition(current.Value, target);
        if (!validation.Succeeded)
            return validation;
        if (!await store.TryTransitionWithAuditAsync(competitionId, current.Value, target, actorId, reason, false, cancellationToken))
            return OperationResult.Failure("lifecycle_conflict", "Competition status changed concurrently.");
        await cache.InvalidateAsync(competitionId, cancellationToken);
        await scheduler.EnqueueLeaderboardRefreshAsync(competitionId, cancellationToken);
        return OperationResult.Success();
    }
}
