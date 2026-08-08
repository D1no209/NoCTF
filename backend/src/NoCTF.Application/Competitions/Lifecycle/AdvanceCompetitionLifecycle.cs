using NoCTF.Domain.Competitions;
using NoCTF.Application.Common;
using NoCTF.Application.Notifications;

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
        CompetitionLifecycleEffects effects,
        CancellationToken cancellationToken) =>
        TryTransitionAsync(competitionId, from, to, cancellationToken);
}

[Flags]
public enum CompetitionLifecycleEffects
{
    None = 0,
    ProvisionRuntimes = 1,
    CleanupRuntimes = 2
}

public enum CompetitionTransitionFailureCode
{
    CompetitionNotFound,
    CompetitionStartGateFailed,
    InvalidSchedule,
    InvalidLifecycleTransition,
    LifecycleConflict
}

/// <summary>Advances published and running competitions using wall-clock deadlines without extending pauses.</summary>
public sealed class AdvanceCompetitionLifecycleUseCase(
    ICompetitionLifecycleStore store,
    CompetitionStartGate? startGate = null)
{
    public async Task<IReadOnlyList<CompetitionLifecycleTransition>> ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var transitions = new List<CompetitionLifecycleTransition>();
        foreach (var competition in await store.GetDueAsync(now, cancellationToken))
        {
            if (competition.Status is CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Paused
                && now >= competition.EndTime)
            {
                if (await store.TryTransitionWithAuditAsync(
                        competition.CompetitionId,
                        competition.Status,
                        CompetitionStatus.Finished,
                        null,
                        "end_time_reached",
                        true,
                        EffectsFor(CompetitionStatus.Finished),
                        cancellationToken))
                    transitions.Add(new(competition.CompetitionId, competition.Status, CompetitionStatus.Finished));
                continue;
            }

            if (competition.Status == CompetitionStatus.Published && now >= competition.StartTime)
            {
                if (startGate is not null
                    && (await startGate.ValidateAsync(
                        competition.CompetitionId,
                        cancellationToken)) is { Count: > 0 })
                    continue;
                if (await store.TryTransitionWithAuditAsync(
                        competition.CompetitionId,
                        competition.Status,
                        CompetitionStatus.Running,
                        null,
                        "start_time_reached",
                        true,
                        EffectsFor(CompetitionStatus.Running),
                        cancellationToken))
                    transitions.Add(new(competition.CompetitionId, competition.Status, CompetitionStatus.Running));
            }
        }
        return transitions;
    }

    internal static CompetitionLifecycleEffects EffectsFor(CompetitionStatus target) =>
        target switch
        {
            CompetitionStatus.Running => CompetitionLifecycleEffects.ProvisionRuntimes,
            CompetitionStatus.Finished => CompetitionLifecycleEffects.CleanupRuntimes,
            _ => CompetitionLifecycleEffects.None
        };
}

public sealed class TransitionCompetitionLifecycle(
    ICompetitionLifecycleStore store,
    ICompetitionLifecycleNotificationPublisher? notifications = null,
    CompetitionStartGate? startGate = null)
{
    public async Task<OperationResult<CompetitionTransitionFailureCode>> ExecuteAsync(
        Guid competitionId,
        CompetitionStatus target,
        Guid? actorId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var current = await store.GetStatusAsync(competitionId, cancellationToken);
        if (current is null)
            return OperationResult<CompetitionTransitionFailureCode>.Failure(
                CompetitionTransitionFailureCode.CompetitionNotFound,
                "Competition was not found.");
        if (target == CompetitionStatus.Running
            && current == CompetitionStatus.Published
            && startGate is not null)
        {
            var errors = await startGate.ValidateAsync(competitionId, cancellationToken);
            if (errors is null)
                return OperationResult<CompetitionTransitionFailureCode>.Failure(
                    CompetitionTransitionFailureCode.CompetitionNotFound,
                    "Competition was not found.");
            if (errors.Count > 0)
                return OperationResult<CompetitionTransitionFailureCode>.Failure(
                    CompetitionTransitionFailureCode.CompetitionStartGateFailed,
                    string.Join(" ", errors.Select(error => error.Message)));
        }
        var validation = CompetitionLifecyclePolicy.ValidateTransition(current.Value, target);
        if (!validation.Succeeded)
            return OperationResult<CompetitionTransitionFailureCode>.Failure(
                validation.FailureCode switch
                {
                    CompetitionLifecyclePolicy.FailureCode.InvalidSchedule => CompetitionTransitionFailureCode.InvalidSchedule,
                    _ => CompetitionTransitionFailureCode.InvalidLifecycleTransition
                },
                validation.ErrorMessage!);
        if (!await store.TryTransitionWithAuditAsync(
                competitionId,
                current.Value,
                target,
                actorId,
                reason,
                false,
                AdvanceCompetitionLifecycleUseCase.EffectsFor(target),
                cancellationToken))
            return OperationResult<CompetitionTransitionFailureCode>.Failure(
                CompetitionTransitionFailureCode.LifecycleConflict,
                "Competition status changed concurrently.");
        if (notifications is not null)
            await notifications.PublishAsync(competitionId, current.Value, target, DateTimeOffset.UtcNow, cancellationToken);
        return OperationResult<CompetitionTransitionFailureCode>.Success();
    }
}
