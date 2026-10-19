namespace NoCTF.Application.Messaging;

public sealed record EvaluateGameplayFact(
    Guid GameplayFactId,
    Guid ExpectedConcurrencyStamp,
    GameplayFactDispatchPurpose Purpose = GameplayFactDispatchPurpose.Intake,
    long RecoveryWindow = 0);

public enum GameplayFactDispatchPurpose { Intake, Recovery, Rejudgement }

public sealed record GameplayFactStateChanged(Guid GameplayFactId, NoCTF.Domain.Gameplay.GameplayFactState State,
    Guid ConcurrencyStamp = default);

public sealed record ReconcileBloodAnnouncements(Guid CompetitionId, long ProjectionVersion = 0);

public sealed record StopSolvedChallengeRuntimes(Guid GameplayFactId, Guid ExpectedConcurrencyStamp);

public sealed record DrainGameplayFactEvaluation(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    Guid? GameplayFactId = null,
    DateTimeOffset? AfterOccurredAt = null,
    Guid? AfterId = null);

public sealed record DrainGameplayFactRejudge(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    Guid? GameplayFactId = null,
    DateTimeOffset? AfterOccurredAt = null,
    Guid? AfterId = null);

public sealed record ForeignTeamFlagDetected(
    Guid CompetitionId,
    Guid GameplayFactId,
    Guid SourceTeamId,
    Guid OwnerTeamId,
    Guid ActorUserId,
    Guid CompetitionChallengeId,
    DateTimeOffset DetectedAt);

public sealed record StaticFlagAcquisitionViolationDetected(
    Guid CompetitionId,
    Guid GameplayFactId,
    Guid SourceTeamId,
    Guid ActorUserId,
    Guid CompetitionChallengeId,
    NoCTF.Domain.Gameplay.GameplayFactFailureCode FailureCode,
    DateTimeOffset DetectedAt);
