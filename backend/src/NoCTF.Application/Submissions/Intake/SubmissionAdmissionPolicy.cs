using NoCTF.Application.Common;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.Submissions.Intake;

/// <summary>Applies transport-independent admission rules using the trusted receive time.</summary>
public static class SubmissionAdmissionPolicy
{
    public static OperationResult Check(SubmissionAdmissionSnapshot snapshot, DateTimeOffset receivedAt)
    {
        if (snapshot.CompetitionDeleted || snapshot.ChallengeDeleted || snapshot.TeamDeleted)
            return OperationResult.Failure("resource_deleted", "The competition, challenge, or team is deleted.");
        if (!snapshot.ChallengePublished)
            return OperationResult.Failure("challenge_unavailable", "The challenge is not published.");
        if (!snapshot.TeamApproved || !snapshot.UserBelongsToTeam)
            return OperationResult.Failure("team_forbidden", "The user cannot submit for this team.");
        if (snapshot.TeamBanned)
            return OperationResult.Failure("team_banned", "The team is banned.");
        if (snapshot.CompetitionStatus != CompetitionStatus.Running)
            return snapshot.CompetitionStatus switch
            {
                CompetitionStatus.Paused => OperationResult.Failure("competition_paused", "The competition is paused."),
                CompetitionStatus.Draft => OperationResult.Failure("competition_not_published", "The competition is not published."),
                CompetitionStatus.Published => OperationResult.Failure("competition_not_started", "The competition has not started."),
                CompetitionStatus.Finished => OperationResult.Failure("competition_finished", "The competition has finished."),
                _ => OperationResult.Failure("competition_unavailable", "The competition is not accepting submissions.")
            };
        if (receivedAt < snapshot.StartTime)
            return OperationResult.Failure("competition_not_started", "The competition has not started.");
        if (receivedAt > snapshot.EndTime)
            return OperationResult.Failure("competition_finished", "The submission arrived after the deadline.");
        return OperationResult.Success();
    }
}
