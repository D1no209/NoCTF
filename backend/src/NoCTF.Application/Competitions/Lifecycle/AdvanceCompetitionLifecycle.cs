using NoCTF.Domain.Competitions;
using NoCTF.Application.Common;
using NoCTF.Application.Notifications;
using NoCTF.Application.Challenges.Images;
using Microsoft.Extensions.Logging;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleSnapshot(
    Guid CompetitionId,
    CompetitionStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime);

public enum CompetitionLifecycleTransitionCommitState
{
    Applied,
    StateConflict,
    ChallengeDefinitionRevisionConflict
}

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

    async Task<CompetitionLifecycleTransitionCommitState> TryTransitionWithChallengeFenceAsync(
        Guid competitionId,
        CompetitionStatus from,
        CompetitionStatus to,
        Guid? actorId,
        string? reason,
        bool automatic,
        CompetitionLifecycleEffects effects,
        IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
        CancellationToken cancellationToken) =>
        await TryTransitionWithAuditAsync(
            competitionId,
            from,
            to,
            actorId,
            reason,
            automatic,
            effects,
            cancellationToken)
            ? CompetitionLifecycleTransitionCommitState.Applied
            : CompetitionLifecycleTransitionCommitState.StateConflict;
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
    LifecycleConflict,
    ChallengeImageInvalid,
    RegistryAuthenticationRequired,
    RegistryAuthenticationFailed,
    RegistryUnavailable,
    RegistryManifestNotFound,
    RegistryManifestInvalid,
    ChallengeDefinitionRevisionConflict
}

/// <summary>Advances published and running competitions using wall-clock deadlines without extending pauses.</summary>
public sealed class AdvanceCompetitionLifecycleUseCase(
    ICompetitionLifecycleStore store,
    CompetitionStartGate? startGate = null,
    PinChallengeImages? imagePinning = null,
    ILogger<AdvanceCompetitionLifecycleUseCase>? logger = null,
    TimeSpan? automaticImagePinningBudget = null)
{
    private static readonly TimeSpan AutomaticImagePinningBudget = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<CompetitionLifecycleTransition>> ExecuteAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var transitions = new List<CompetitionLifecycleTransition>();
        var due = await store.GetDueAsync(now, cancellationToken);
        foreach (var competition in due.Where(item =>
                     (item.Status is CompetitionStatus.Published
                         or CompetitionStatus.Running
                         or CompetitionStatus.Paused)
                     && now >= item.EndTime))
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
                transitions.Add(new(
                    competition.CompetitionId,
                    competition.Status,
                    CompetitionStatus.Finished));
        }

        using var pinningBudget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        pinningBudget.CancelAfter(
            automaticImagePinningBudget ?? AutomaticImagePinningBudget);
        foreach (var competition in due.Where(item =>
                     item.Status == CompetitionStatus.Published
                     && now >= item.StartTime
                     && now < item.EndTime))
        {
            if (startGate is not null
                && (await startGate.ValidateAsync(
                        competition.CompetitionId,
                        cancellationToken))?
                    .Any(error => error.Code != StartGateFailureCode.RuntimeImageNotPinned)
                    == true)
                continue;
            ChallengeImagePinResult? pinning = null;
            if (imagePinning is not null)
            {
                try
                {
                    pinning = await imagePinning.PinCompetitionAsync(
                        competition.CompetitionId,
                        now,
                        pinningBudget.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    logger?.LogWarning(
                        "Automatic competition image pinning failed with {FailureCode} for competition {CompetitionId} challenge {ChallengeId}",
                        ChallengeImagePinFailureCode.RegistryUnavailable,
                        competition.CompetitionId,
                        null);
                    break;
                }
                if (!pinning.Succeeded)
                {
                    var error = pinning.Errors[0];
                    logger?.LogWarning(
                        "Automatic competition image pinning failed with {FailureCode} for competition {CompetitionId} challenge {ChallengeId}",
                        error.Code,
                        competition.CompetitionId,
                        error.ChallengeId);
                    continue;
                }
            }
            if (startGate is not null
                && (await startGate.ValidateAsync(
                    competition.CompetitionId,
                    cancellationToken)) is { Count: > 0 })
                continue;
            var committed = pinning is null
                ? await store.TryTransitionWithAuditAsync(
                    competition.CompetitionId,
                    competition.Status,
                    CompetitionStatus.Running,
                    null,
                    "start_time_reached",
                    true,
                    EffectsFor(CompetitionStatus.Running),
                    cancellationToken)
                        ? CompetitionLifecycleTransitionCommitState.Applied
                        : CompetitionLifecycleTransitionCommitState.StateConflict
                : await store.TryTransitionWithChallengeFenceAsync(
                    competition.CompetitionId,
                    competition.Status,
                    CompetitionStatus.Running,
                    null,
                    "start_time_reached",
                    true,
                    EffectsFor(CompetitionStatus.Running),
                    pinning.ChallengeRevisions!,
                    cancellationToken);
            if (committed == CompetitionLifecycleTransitionCommitState.Applied)
                transitions.Add(new(
                    competition.CompetitionId,
                    competition.Status,
                    CompetitionStatus.Running));
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
    CompetitionStartGate? startGate = null,
    PinChallengeImages? imagePinning = null)
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
        var validation = CompetitionLifecyclePolicy.ValidateTransition(current.Value, target);
        if (!validation.Succeeded)
            return OperationResult<CompetitionTransitionFailureCode>.Failure(
                validation.FailureCode switch
                {
                    CompetitionLifecyclePolicy.FailureCode.InvalidSchedule => CompetitionTransitionFailureCode.InvalidSchedule,
                    _ => CompetitionTransitionFailureCode.InvalidLifecycleTransition
                },
                validation.ErrorMessage!);
        if (target == CompetitionStatus.Running
            && current == CompetitionStatus.Published
            && startGate is not null)
        {
            var preflightErrors = await startGate.ValidateAsync(
                competitionId,
                cancellationToken);
            if (preflightErrors is null)
                return OperationResult<CompetitionTransitionFailureCode>.Failure(
                    CompetitionTransitionFailureCode.CompetitionNotFound,
                    "Competition was not found.");
            var blockingErrors = preflightErrors
                .Where(error => error.Code != StartGateFailureCode.RuntimeImageNotPinned)
                .ToArray();
            if (blockingErrors.Length > 0)
                return OperationResult<CompetitionTransitionFailureCode>.Failure(
                    CompetitionTransitionFailureCode.CompetitionStartGateFailed,
                    string.Join(" ", blockingErrors.Select(error => error.Message)));
        }
        ChallengeImagePinResult? pinning = null;
        if ((target is CompetitionStatus.Published or CompetitionStatus.Running)
            && imagePinning is not null)
        {
            pinning = await imagePinning.PinCompetitionAsync(
                competitionId,
                DateTimeOffset.UtcNow,
                cancellationToken);
            if (!pinning.Succeeded)
            {
                var error = pinning.Errors[0];
                return OperationResult<CompetitionTransitionFailureCode>.Failure(
                    Map(error.Code),
                    error.Message);
            }
        }
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
        var committed = pinning is null
            ? await store.TryTransitionWithAuditAsync(
                competitionId,
                current.Value,
                target,
                actorId,
                reason,
                false,
                AdvanceCompetitionLifecycleUseCase.EffectsFor(target),
                cancellationToken)
                    ? CompetitionLifecycleTransitionCommitState.Applied
                    : CompetitionLifecycleTransitionCommitState.StateConflict
            : await store.TryTransitionWithChallengeFenceAsync(
                competitionId,
                current.Value,
                target,
                actorId,
                reason,
                false,
                AdvanceCompetitionLifecycleUseCase.EffectsFor(target),
                pinning.ChallengeRevisions!,
                cancellationToken);
        if (committed != CompetitionLifecycleTransitionCommitState.Applied)
            return OperationResult<CompetitionTransitionFailureCode>.Failure(
                committed == CompetitionLifecycleTransitionCommitState.ChallengeDefinitionRevisionConflict
                    ? CompetitionTransitionFailureCode.ChallengeDefinitionRevisionConflict
                    : CompetitionTransitionFailureCode.LifecycleConflict,
                committed == CompetitionLifecycleTransitionCommitState.ChallengeDefinitionRevisionConflict
                    ? "Challenge definition changed after its images were resolved."
                    : "Competition status changed concurrently.");
        if (notifications is not null)
            await notifications.PublishAsync(competitionId, current.Value, target, DateTimeOffset.UtcNow, cancellationToken);
        return OperationResult<CompetitionTransitionFailureCode>.Success();
    }

    private static CompetitionTransitionFailureCode Map(
        ChallengeImagePinFailureCode failure) =>
        failure switch
        {
            ChallengeImagePinFailureCode.CompetitionNotFound =>
                CompetitionTransitionFailureCode.CompetitionNotFound,
            ChallengeImagePinFailureCode.InvalidDefinition
                or ChallengeImagePinFailureCode.InvalidImageReference
                or ChallengeImagePinFailureCode.ChallengeNotFound =>
                CompetitionTransitionFailureCode.ChallengeImageInvalid,
            ChallengeImagePinFailureCode.RegistryAuthenticationRequired =>
                CompetitionTransitionFailureCode.RegistryAuthenticationRequired,
            ChallengeImagePinFailureCode.RegistryAuthenticationFailed =>
                CompetitionTransitionFailureCode.RegistryAuthenticationFailed,
            ChallengeImagePinFailureCode.RegistryUnavailable =>
                CompetitionTransitionFailureCode.RegistryUnavailable,
            ChallengeImagePinFailureCode.RegistryManifestNotFound =>
                CompetitionTransitionFailureCode.RegistryManifestNotFound,
            ChallengeImagePinFailureCode.RegistryManifestInvalid =>
                CompetitionTransitionFailureCode.RegistryManifestInvalid,
            ChallengeImagePinFailureCode.RevisionConflict =>
                CompetitionTransitionFailureCode.ChallengeDefinitionRevisionConflict,
            _ => CompetitionTransitionFailureCode.ChallengeImageInvalid
        };
}
