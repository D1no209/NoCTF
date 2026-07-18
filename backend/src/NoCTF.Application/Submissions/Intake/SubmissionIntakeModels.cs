using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Intake;

public sealed record SubmissionAdmissionSnapshot(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    long Revision,
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

public sealed record SubmissionAccepted(Guid SubmissionId, DateTimeOffset ReceivedAt);

public sealed record FlagSubmissionCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    string Flag,
    string IpAddress,
    DateTimeOffset ReceivedAt);

public sealed record FixSubmissionCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid ChallengeId,
    Guid UserId,
    Events.FixArchiveReference Archive,
    string IpAddress,
    DateTimeOffset ReceivedAt);
