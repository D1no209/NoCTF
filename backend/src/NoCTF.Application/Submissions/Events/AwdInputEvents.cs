namespace NoCTF.Application.Submissions.Events;

public enum AwdServiceObservation
{
    Up,
    Down,
    PlatformError
}

public sealed record AwdFlagRotated(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    string Flag,
    DateTimeOffset OccurredAt) : ISubmissionStreamEvent;

public sealed record AwdServiceChecked(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    AwdServiceObservation Observation,
    DateTimeOffset OccurredAt) : ISubmissionStreamEvent;
