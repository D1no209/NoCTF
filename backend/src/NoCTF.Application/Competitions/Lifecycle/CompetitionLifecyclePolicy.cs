using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleTransition(
    Guid CompetitionId,
    CompetitionStatus From,
    CompetitionStatus To);

public static class CompetitionLifecyclePolicy
{
    public enum FailureCode
    {
        InvalidSchedule,
        InvalidLifecycleTransition
    }

    public static OperationResult<FailureCode> ValidateSchedule(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        return startTime < endTime
            ? OperationResult<FailureCode>.Success()
            : OperationResult<FailureCode>.Failure(
                FailureCode.InvalidSchedule,
                "Competition start time must be before its end time.");
    }

    public static OperationResult<FailureCode> ValidateTransition(CompetitionStatus from, CompetitionStatus to) =>
        (from, to) switch
        {
            (CompetitionStatus.Draft, CompetitionStatus.Visible) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Visible, CompetitionStatus.Published) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Published, CompetitionStatus.Visible) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Published, CompetitionStatus.Running) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Running, CompetitionStatus.Paused) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Paused, CompetitionStatus.Running) => OperationResult<FailureCode>.Success(),
            (CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Paused, CompetitionStatus.Finished) => OperationResult<FailureCode>.Success(),
            _ => OperationResult<FailureCode>.Failure(
                FailureCode.InvalidLifecycleTransition,
                $"Cannot transition competition from {from} to {to}.")
        };
}
