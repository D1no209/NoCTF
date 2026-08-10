using NoCTF.Application.Common;
using NoCTF.Application.GameplayFacts.Intake;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public sealed class SubmitFix(IGameplayFactIntakeStore store, IGameplayFactAdmissionModePolicy modePolicy)
{
    public async Task<OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>> ExecuteAsync(
        FixGameplayFactCommand command,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await store.LoadAdmissionAsync(
            command.CompetitionId, command.CompetitionChallengeId, command.UserId, cancellationToken);
        if (snapshot is null)
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.GameplayFactScopeNotFound, "GameplayFact scope was not found.");
        var rules = modePolicy.GetRules(
            snapshot.Mode, snapshot.CompetitionConfigurationJson, snapshot.ChallengeConfigurationJson);
        var admission = GameplayFactAdmissionPolicy.Check(
            snapshot, GameplayFactKind.FixAttempt, rules, command.OccurredAt);
        if (!admission.Succeeded)
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                admission.FailureCode!.Value, admission.ErrorMessage!);

        var gameplayFactId = Guid.CreateVersion7(command.OccurredAt);
        var accepted = await store.TryAcceptFixAsync(
            new(
                gameplayFactId,
                command.CompetitionId,
                snapshot.TeamId,
                command.CompetitionChallengeId,
                command.UserId,
                command.PatchUploadId,
                command.OccurredAt),
            snapshot,
            rules.MaxFixAttempts,
            cancellationToken);
        return accepted.State switch
        {
            GameplayFactAcceptanceState.Created =>
                OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Success(new(accepted.GameplayFactId!.Value, accepted.OccurredAt!.Value)),
            GameplayFactAcceptanceState.PatchUploadUnavailable =>
                OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                    GameplayFactAdmissionFailureCode.PatchUploadNotFound, "The patch upload was not found or has already been consumed."),
            GameplayFactAcceptanceState.AttemptsExhausted =>
                OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                    GameplayFactAdmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached."),
            _ => OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.GameplayFactConcurrency, "The submission could not be accepted because its scope changed.")
        };
    }
}
