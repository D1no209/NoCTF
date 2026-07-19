using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed record SubmissionAdmissionSnapshot(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    GameMode Mode,
    int CompetitionConfigurationRevision,
    int ChallengeConfigurationRevision,
    string CompetitionConfigurationJson,
    string ChallengeConfigurationJson,
    int AcceptedFlagAttempts,
    int AcceptedFixAttempts,
    CompetitionStatus CompetitionStatus,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool CompetitionDeleted,
    bool ChallengeDeleted,
    bool ChallengePublished,
    bool TeamDeleted,
    bool TeamBanned,
    bool TeamApproved,
    bool UserBelongsToTeam);

public sealed record SubmissionAdmissionRules(
    bool AllowsFlag,
    bool AllowsFix,
    int? MaxFlagAttempts,
    int? MaxFixAttempts);

public interface ISubmissionAdmissionModePolicy
{
    SubmissionAdmissionRules GetRules(
        GameMode mode,
        string competitionConfigurationJson,
        string challengeConfigurationJson);
}

public enum SubmissionAcceptanceState
{
    Created,
    Existing,
    IdempotencyConflict,
    AttemptsExhausted,
    SnapshotChanged,
    UploadUnavailable
}

public sealed record SubmissionAcceptanceResult(
    SubmissionAcceptanceState State,
    Guid? SubmissionId = null,
    DateTimeOffset? ReceivedAt = null);

public sealed record SubmissionAccepted(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record FlagSubmissionCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    string Flag,
    string IdempotencyKey,
    string IpAddress,
    DateTimeOffset ReceivedAt);

public sealed record FixSubmissionCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    Guid UploadId,
    string IdempotencyKey,
    string IpAddress,
    DateTimeOffset ReceivedAt);
