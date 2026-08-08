using System.Globalization;
using NoCTF.Application.Common;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed record ManualAdjustmentCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    int Delta,
    Guid UserId,
    DateTimeOffset ReceivedAt);

public sealed class CreateManualAdjustment(ISubmissionIntakeStore store)
{
    public async Task<OperationResult<SubmissionAccepted, SubmissionFailureCode>> ExecuteAsync(
        ManualAdjustmentCommand command,
        CancellationToken ct = default)
    {
        if (command.Delta == 0)
            return OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                SubmissionFailureCode.FlagInvalid, "Manual adjustment delta cannot be zero.");
        var submissionId = Guid.CreateVersion7(command.ReceivedAt);
        var result = await store.TryAcceptManualAdjustmentAsync(
            new(submissionId, command.CompetitionId, command.TeamId,
                command.CompetitionChallengeId, command.UserId, command.Delta,
                command.ReceivedAt), ct);
        return result.State == SubmissionAcceptanceState.Created
            ? OperationResult<SubmissionAccepted, SubmissionFailureCode>.Success(
                new(result.SubmissionId!.Value, result.ReceivedAt!.Value))
            : OperationResult<SubmissionAccepted, SubmissionFailureCode>.Failure(
                SubmissionFailureCode.SubmissionScopeNotFound,
                "The team or competition challenge was not found.");
    }
}
