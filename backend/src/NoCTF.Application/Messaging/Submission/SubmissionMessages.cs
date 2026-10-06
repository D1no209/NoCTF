namespace NoCTF.Application.Messaging;

public sealed record EvaluateGameplayFact(
    Guid GameplayFactId,
    Guid DispatchAttemptId = default);

public sealed record GameplayFactStateChanged(Guid GameplayFactId, NoCTF.Domain.Gameplay.GameplayFactState State);

public sealed record DrainGameplayFactEvaluation(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    Guid? GameplayFactId = null);

public sealed record DrainGameplayFactRejudge(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    DateTimeOffset Cutoff,
    Guid? GameplayFactId = null);

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
