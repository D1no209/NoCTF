using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Applies transport-independent admission rules using the trusted receive time.</summary>
public static class SubmissionAdmissionPolicy
{
    public static OperationResult Check(
        SubmissionAdmissionSnapshot snapshot,
        SubmissionKind kind,
        SubmissionAdmissionRules rules,
        DateTimeOffset receivedAt)
    {
        if (snapshot.CompetitionDeleted || snapshot.ChallengeDeleted || snapshot.TeamDeleted)
            return OperationResult.Failure("resource_deleted", "The competition, challenge, or team is deleted.");
        if (!snapshot.ChallengePublished)
            return OperationResult.Failure("challenge_unavailable", "The challenge is not published.");
        if (!snapshot.TeamApproved || !snapshot.UserBelongsToTeam)
            return OperationResult.Failure("team_forbidden", "The user cannot submit for this team.");
        if (snapshot.TeamBanned)
            return OperationResult.Failure("team_banned", "The team is banned.");
        if (kind is SubmissionKind.Flag or SubmissionKind.Break && !rules.AllowsFlag
            || kind == SubmissionKind.Fix && !rules.AllowsFix)
            return OperationResult.Failure("submission_kind_unsupported", "This game mode does not accept this submission kind.");
        if (snapshot.CompetitionStatus != CompetitionStatus.Running)
            return snapshot.CompetitionStatus switch
            {
                CompetitionStatus.Paused => OperationResult.Failure("competition_paused", "The competition is paused."),
                CompetitionStatus.Draft => OperationResult.Failure("competition_not_published", "The competition is not published."),
                CompetitionStatus.Published => OperationResult.Failure("competition_not_started", "The competition has not started."),
                CompetitionStatus.Finished => OperationResult.Failure("competition_finished", "The competition has finished."),
                _ => OperationResult.Failure("competition_unavailable", "The competition is not accepting submissions.")
            };
        if (receivedAt < snapshot.StartAt)
            return OperationResult.Failure("competition_not_started", "The competition has not started.");
        if (receivedAt >= snapshot.EndAt)
            return OperationResult.Failure("competition_finished", "The submission arrived after the deadline.");
        if (kind == SubmissionKind.Fix
            && rules.RequireBreakBeforeFix
            && !snapshot.HasCorrectBreak)
            return OperationResult.Failure(
                "break_required",
                "A correct Break submission is required before submitting a Fix.");
        var maxAttempts = kind is SubmissionKind.Flag or SubmissionKind.Break ? rules.MaxFlagAttempts : rules.MaxFixAttempts;
        var acceptedAttempts = kind is SubmissionKind.Flag or SubmissionKind.Break
            ? snapshot.AcceptedFlagAttempts
            : snapshot.AcceptedFixAttempts;
        if (maxAttempts is > 0 && acceptedAttempts >= maxAttempts)
            return OperationResult.Failure("attempts_exhausted", "The maximum number of accepted attempts has been reached.");
        return OperationResult.Success();
    }
}
