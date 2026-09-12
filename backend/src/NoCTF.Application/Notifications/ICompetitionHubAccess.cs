namespace NoCTF.Application.Notifications;

public sealed record CompetitionHubAccessDecision(bool IsStaff);

public interface ICompetitionHubAccess
{
    Task<CompetitionHubAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken);
}
