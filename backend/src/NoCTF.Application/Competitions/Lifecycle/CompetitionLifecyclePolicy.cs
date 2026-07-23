using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Competitions.Lifecycle;

public sealed record CompetitionLifecycleTransition(
    Guid CompetitionId,
    CompetitionStatus From,
    CompetitionStatus To);

public static class CompetitionLifecyclePolicy
{
    public static OperationResult ValidateSchedule(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        return startTime < endTime
            ? OperationResult.Success()
            : OperationResult.Failure("invalid_schedule", "Competition start time must be before its end time.");
    }

    public static OperationResult ValidateTransition(CompetitionStatus from, CompetitionStatus to) =>
        (from, to) switch
        {
            (CompetitionStatus.Draft, CompetitionStatus.Visible) => OperationResult.Success(),
            (CompetitionStatus.Visible, CompetitionStatus.Published) => OperationResult.Success(),
            (CompetitionStatus.Published, CompetitionStatus.Visible) => OperationResult.Success(),
            (CompetitionStatus.Published, CompetitionStatus.Running) => OperationResult.Success(),
            (CompetitionStatus.Running, CompetitionStatus.Paused) => OperationResult.Success(),
            (CompetitionStatus.Paused, CompetitionStatus.Running) => OperationResult.Success(),
            (CompetitionStatus.Published or CompetitionStatus.Running or CompetitionStatus.Paused, CompetitionStatus.Finished) => OperationResult.Success(),
            _ => OperationResult.Failure("invalid_lifecycle_transition", $"Cannot transition competition from {from} to {to}.")
        };
}
