using System.Globalization;
using NoCTF.Application.Common;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public sealed record ManualAdjustmentCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    int Delta,
    Guid UserId,
    DateTimeOffset OccurredAt);

public sealed class CreateManualAdjustment(IGameplayFactIntakeStore store)
{
    public async Task<OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>> ExecuteAsync(
        ManualAdjustmentCommand command,
        CancellationToken ct = default)
    {
        if (command.Delta == 0)
            return OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.FlagInvalid, "Manual adjustment delta cannot be zero.");
        var gameplayFactId = Guid.CreateVersion7(command.OccurredAt);
        var result = await store.TryAcceptManualAdjustmentAsync(
            new(gameplayFactId, command.CompetitionId, command.TeamId,
                command.CompetitionChallengeId, command.UserId, command.Delta,
                command.OccurredAt), ct);
        return result.State == GameplayFactAcceptanceState.Created
            ? OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Success(
                new(result.GameplayFactId!.Value, result.OccurredAt!.Value))
            : OperationResult<GameplayFactAccepted, GameplayFactAdmissionFailureCode>.Failure(
                GameplayFactAdmissionFailureCode.GameplayFactScopeNotFound,
                "The team or competition challenge was not found.");
    }
}
