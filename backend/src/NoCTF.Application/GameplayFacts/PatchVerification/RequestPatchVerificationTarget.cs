using NoCTF.Application.Common;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Runtime;

namespace NoCTF.Application.GameplayFacts.PatchVerification;

public enum PatchVerificationTargetRequestState
{
    Created,
    ScopeNotFound,
    FeatureUnavailable,
    ActiveTargetExists,
    AchievementAlreadySucceeded,
    AttemptsExhausted,
    RuntimeQuotaExceeded,
    InvalidConfiguration,
    ConcurrencyConflict
}

public enum PatchVerificationTargetFailureCode
{
    PatchVerificationNotAvailable,
    ExperimentalFeatureDisabled,
    ActiveTargetExists,
    PatchAlreadyVerified,
    PatchAttemptsExhausted,
    RuntimeQuotaExceeded,
    InvalidRuntimeConfiguration,
    TargetConcurrency
}

public sealed record PatchVerificationTargetRequestResult(
    PatchVerificationTargetRequestState State,
    Guid? RuntimeInstanceId = null,
    RuntimeState? RuntimeState = null);

public sealed record PatchVerificationParticipantState(
    bool Available,
    int MaximumAttempts,
    int AcceptedAttempts,
    int RemainingAttempts,
    GameplayFactState? VerificationState,
    GameplayFactResult? VerificationResult,
    GameplayFactFailureCode? VerificationFailureCode,
    Guid? RuntimeInstanceId,
    RuntimeState? RuntimeState,
    PatchVerificationTargetFailureCode? UnavailableCode = null);

public interface IPatchVerificationTargetStore
{
    Task<PatchVerificationTargetRequestResult> TryCreateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<PatchVerificationParticipantState?> GetStateAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record RequestedPatchVerificationTarget(
    Guid RuntimeInstanceId,
    RuntimeState State);

public sealed class RequestPatchVerificationTarget(IPatchVerificationTargetStore store)
{
    public async Task<OperationResult<RequestedPatchVerificationTarget,
        PatchVerificationTargetFailureCode>> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var result = await store.TryCreateAsync(
            competitionId,
            competitionChallengeId,
            userId,
            now,
            ct);
        return result.State switch
        {
            PatchVerificationTargetRequestState.Created =>
                OperationResult<RequestedPatchVerificationTarget,
                    PatchVerificationTargetFailureCode>.Success(new(
                        result.RuntimeInstanceId!.Value,
                        result.RuntimeState!.Value)),
            PatchVerificationTargetRequestState.FeatureUnavailable => Failure(
                PatchVerificationTargetFailureCode.ExperimentalFeatureDisabled,
                "CTF PatchVerification is disabled for this competition challenge."),
            PatchVerificationTargetRequestState.ActiveTargetExists => Failure(
                PatchVerificationTargetFailureCode.ActiveTargetExists,
                "An active PatchVerification target already exists."),
            PatchVerificationTargetRequestState.AchievementAlreadySucceeded => Failure(
                PatchVerificationTargetFailureCode.PatchAlreadyVerified,
                "This PatchVerification challenge has already been solved."),
            PatchVerificationTargetRequestState.AttemptsExhausted => Failure(
                PatchVerificationTargetFailureCode.PatchAttemptsExhausted,
                "The maximum number of Patch attempts has been reached."),
            PatchVerificationTargetRequestState.RuntimeQuotaExceeded => Failure(
                PatchVerificationTargetFailureCode.RuntimeQuotaExceeded,
                "The team Runtime challenge quota has been reached."),
            PatchVerificationTargetRequestState.InvalidConfiguration => Failure(
                PatchVerificationTargetFailureCode.InvalidRuntimeConfiguration,
                "The PatchVerification target configuration is invalid."),
            PatchVerificationTargetRequestState.ConcurrencyConflict => Failure(
                PatchVerificationTargetFailureCode.TargetConcurrency,
                "The PatchVerification target changed concurrently."),
            _ => Failure(
                PatchVerificationTargetFailureCode.PatchVerificationNotAvailable,
                "PatchVerification is unavailable for this team and challenge.")
        };
    }

    private static OperationResult<RequestedPatchVerificationTarget,
        PatchVerificationTargetFailureCode> Failure(
        PatchVerificationTargetFailureCode code,
        string message) =>
        OperationResult<RequestedPatchVerificationTarget,
            PatchVerificationTargetFailureCode>.Failure(code, message);
}

public sealed class GetPatchVerificationState(IPatchVerificationTargetStore store)
{
    public Task<PatchVerificationParticipantState?> ExecuteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken ct = default) =>
        store.GetStateAsync(competitionId, competitionChallengeId, userId, ct);
}
