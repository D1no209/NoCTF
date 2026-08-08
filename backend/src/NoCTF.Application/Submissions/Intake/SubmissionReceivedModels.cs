using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public sealed record FlagSubmissionReceived(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    SubmissionKind Kind,
    string SubmittedFlag,
    byte[] SubmittedFlagSha256,
    DateTimeOffset ReceivedAt)
{
    public override string ToString() =>
        $"{nameof(FlagSubmissionReceived)} {{ SubmissionId = {SubmissionId}, Flag = [REDACTED] }}";
}

public sealed record FixSubmissionReceived(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    Guid PatchUploadId,
    DateTimeOffset ReceivedAt);

public sealed record HintUnlockSubmissionReceived(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    Guid HintId,
    DateTimeOffset ReceivedAt);

public sealed record ManualAdjustmentSubmissionReceived(
    Guid SubmissionId,
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid UserId,
    int Delta,
    DateTimeOffset ReceivedAt);
