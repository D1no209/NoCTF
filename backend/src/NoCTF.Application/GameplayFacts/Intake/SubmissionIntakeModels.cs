using NoCTF.Domain.Competitions;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Intake;

public sealed record GameplayFactAdmissionSnapshot(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    GameMode Mode,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    int AcceptedFlagAttempts,
    int AcceptedFixAttempts,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    bool CompetitionDeleted,
    bool ChallengeDeleted,
    bool ChallengePublished,
    bool TeamDeleted,
    bool TeamBanned,
    bool TeamApproved,
    bool UserBelongsToTeam,
    bool HasCorrectBreak = false,
    bool HasCorrectFix = false);

public sealed record GameplayFactAdmissionRules(
    bool AllowsFlag,
    bool AllowsFix,
    int? MaxFlagAttempts,
    int? MaxFixAttempts,
    bool RequireBreakBeforeFix = false);

public interface IGameplayFactAdmissionModePolicy
{
    GameplayFactAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson);
}

public enum GameplayFactAcceptanceState
{
    Created,
    AchievementAlreadySucceeded,
    AttemptsExhausted,
    AdmissionRejected
}

public sealed record GameplayFactAcceptanceResult(
    GameplayFactAcceptanceState State,
    Guid? GameplayFactId = null,
    DateTimeOffset? OccurredAt = null);

public sealed record GameplayFactAccepted(Guid GameplayFactId, DateTimeOffset OccurredAt);

public sealed record FlagAttemptBudget(int Maximum, int Accepted, int Remaining);

public sealed record FlagGameplayFactCommand(
    Guid CompetitionId,
    Guid CompetitionChallengeId,
    Guid UserId,
    string Flag,
    DateTimeOffset OccurredAt);
