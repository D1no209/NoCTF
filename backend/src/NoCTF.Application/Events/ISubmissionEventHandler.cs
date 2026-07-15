namespace NoCTF.Application.Events;

/// <summary>
/// Context passed to the submission event handler after a successful solve.
/// </summary>
public record SubmissionSolvedEvent(
    Guid CompetitionId,
    Guid ChallengeId,
    string ChallengeName,
    Guid TeamId,
    string TeamName,
    bool IsFirstBlood,
    int PointsAwarded,
    int SolveRank = 0,
    Guid? SubmissionId = null,
    Guid? UserId = null,
    Guid? BloodScopeId = null,
    DateTime? OccurredAt = null);

/// <summary>
/// Handles post-solve side effects: leaderboard sync and SignalR notifications.
/// </summary>
public interface ISubmissionEventHandler
{
    Task HandleAsync(SubmissionSolvedEvent solvedEvent, CancellationToken ct = default);
}
