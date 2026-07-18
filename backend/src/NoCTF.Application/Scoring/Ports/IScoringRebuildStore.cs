using NoCTF.Application.Scoring.Evaluation;

namespace NoCTF.Application.Scoring.Ports;

public sealed record ScoringRebuildLease(
    Guid CompetitionId,
    Guid StagingStreamId,
    long SubmissionHighWaterMark);

public enum ScoringRebuildReason
{
    AwdFlagRotated,
    AwdServiceChecked,
    KohControlObserved,
    SystemScoringInput,
    PenetrationStageCompleted,
    TeamBanned,
    TeamUnbanned
}

public interface IScoringRebuildStore
{
    Task<ScoringRebuildLease> BeginAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<ScoringContext> LoadContextAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionEventEnvelope>> ReadSubmissionEventsAsync(
        Guid competitionId,
        long afterSequence,
        long throughSequence,
        CancellationToken cancellationToken);
    Task ReplaceStagingEventsAsync(
        ScoringRebuildLease lease,
        IReadOnlyList<DerivedScoringEvent> events,
        CancellationToken cancellationToken);
    Task<long> GetSubmissionHighWaterMarkAsync(Guid competitionId, CancellationToken cancellationToken);
    Task ActivateAsync(
        ScoringRebuildLease lease,
        ScoringContext context,
        long caughtUpSequence,
        CancellationToken cancellationToken);
}

public interface IScoringRebuildQueue
{
    Task EnqueueAsync(Guid competitionId, ScoringRebuildReason reason, CancellationToken cancellationToken);
}
