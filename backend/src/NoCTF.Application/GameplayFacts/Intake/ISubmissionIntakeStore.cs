using NoCTF.Application.GameplayFacts.Intake;

namespace NoCTF.Application.GameplayFacts.Intake;

public interface IGameplayFactIntakeStore
{
    Task<GameplayFactAdmissionSnapshot?> LoadAdmissionAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<GameplayFactAcceptanceResult> TryAcceptFlagAsync(
        FlagGameplayFactReceived received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GameplayFactAcceptanceResult>> TryAcceptFlagsAsync(
        IReadOnlyList<FlagGameplayFactReceived> received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<GameplayFactAcceptanceResult> TryAcceptFixAsync(
        FixGameplayFactReceived received,
        GameplayFactAdmissionSnapshot snapshot,
        int? maxAttempts,
        CancellationToken cancellationToken);

    Task<GameplayFactAcceptanceResult> TryAcceptHintUnlockAsync(
        HintUnlockGameplayFactReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.SnapshotChanged));

    Task<GameplayFactAcceptanceResult> TryAcceptManualAdjustmentAsync(
        ManualAdjustmentGameplayFactReceived received,
        CancellationToken cancellationToken) =>
        Task.FromResult(new GameplayFactAcceptanceResult(GameplayFactAcceptanceState.SnapshotChanged));
}
