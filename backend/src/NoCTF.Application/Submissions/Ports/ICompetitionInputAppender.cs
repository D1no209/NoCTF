using NoCTF.Application.Submissions.Events;

namespace NoCTF.Application.Submissions.Ports;

public sealed record AwdFlagInput(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    string Flag,
    DateTimeOffset OccurredAt,
    string IdempotencyKey);

public sealed record AwdServiceInput(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    int Round,
    AwdServiceObservation Observation,
    DateTimeOffset OccurredAt,
    string IdempotencyKey);

public sealed record KohInput(
    Guid CompetitionId,
    Guid ChallengeId,
    Guid? ControllerTeamId,
    string? ControllerName,
    bool IsAuthoritative,
    DateTimeOffset ObservedAt,
    string IdempotencyKey);

public sealed record SystemScoreInput(
    Guid CompetitionId,
    Guid TeamId,
    SystemScoreKind Kind,
    long Points,
    DateTimeOffset OccurredAt,
    string IdempotencyKey);

public sealed record PenetrationStageInput(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid StageId,
    DateTimeOffset OccurredAt,
    string IdempotencyKey);

public interface ICompetitionInputAppender
{
    Task<bool> AppendAsync(AwdFlagInput input, CancellationToken cancellationToken);
    Task<bool> AppendAsync(AwdServiceInput input, CancellationToken cancellationToken);
    Task<bool> AppendAsync(KohInput input, CancellationToken cancellationToken);
    Task<bool> AppendAsync(SystemScoreInput input, CancellationToken cancellationToken);
    Task<bool> AppendAsync(PenetrationStageInput input, CancellationToken cancellationToken);
}
