using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

public enum SubmissionFailureCode
{
    ResourceDeleted,
    ChallengeUnavailable,
    TeamForbidden,
    TeamBanned,
    SubmissionKindUnsupported,
    CompetitionPaused,
    CompetitionNotPublished,
    CompetitionNotStarted,
    CompetitionFinished,
    CompetitionUnavailable,
    BreakRequired,
    AttemptsExhausted,
    FlagInvalid,
    FlagBatchNotSupported,
    SubmissionScopeNotFound,
    SubmissionConcurrency,
    PatchUploadNotFound
}

/// <summary>Applies transport-independent admission rules using the trusted receive time.</summary>
public static class SubmissionAdmissionPolicy
{
    public static OperationResult<SubmissionFailureCode> Check(
        SubmissionAdmissionSnapshot snapshot,
        SubmissionKind kind,
        SubmissionAdmissionRules rules,
        DateTimeOffset receivedAt)
    {
        if (snapshot.CompetitionDeleted || snapshot.ChallengeDeleted || snapshot.TeamDeleted)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.ResourceDeleted, "The competition, challenge, or team is deleted.");
        if (!snapshot.ChallengePublished)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.ChallengeUnavailable, "The challenge is not published.");
        if (!snapshot.TeamApproved || !snapshot.UserBelongsToTeam)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.TeamForbidden, "The user cannot submit for this team.");
        if (snapshot.TeamBanned)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.TeamBanned, "The team is banned.");
        if (kind is SubmissionKind.Flag or SubmissionKind.Break && !rules.AllowsFlag
            || kind == SubmissionKind.Fix && !rules.AllowsFix)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.SubmissionKindUnsupported, "This game mode does not accept this submission kind.");
        if (snapshot.CompetitionStatus != CompetitionStatus.Running)
            return snapshot.CompetitionStatus switch
            {
                CompetitionStatus.Paused => OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionPaused, "The competition is paused."),
                CompetitionStatus.Draft => OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionNotPublished, "The competition is not published."),
                CompetitionStatus.Published => OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionNotStarted, "The competition has not started."),
                CompetitionStatus.Finished => OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionFinished, "The competition has finished."),
                _ => OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionUnavailable, "The competition is not accepting submissions.")
            };
        if (receivedAt < snapshot.StartAt)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionNotStarted, "The competition has not started.");
        if (receivedAt >= snapshot.EndAt)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.CompetitionFinished, "The submission arrived after the deadline.");
        if (kind == SubmissionKind.Fix
            && rules.RequireBreakBeforeFix
            && !snapshot.HasCorrectBreak)
            return OperationResult<SubmissionFailureCode>.Failure(
                SubmissionFailureCode.BreakRequired,
                "A correct Break submission is required before submitting a Fix.");
        var maxAttempts = kind is SubmissionKind.Flag or SubmissionKind.Break ? rules.MaxFlagAttempts : rules.MaxFixAttempts;
        var acceptedAttempts = kind is SubmissionKind.Flag or SubmissionKind.Break
            ? snapshot.AcceptedFlagAttempts
            : snapshot.AcceptedFixAttempts;
        if (maxAttempts is > 0 && acceptedAttempts >= maxAttempts)
            return OperationResult<SubmissionFailureCode>.Failure(SubmissionFailureCode.AttemptsExhausted, "The maximum number of accepted attempts has been reached.");
        return OperationResult<SubmissionFailureCode>.Success();
    }
}
