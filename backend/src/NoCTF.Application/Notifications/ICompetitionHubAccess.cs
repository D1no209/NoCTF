namespace NoCTF.Application.Notifications;

public interface ICompetitionHubAccess
{
    Task<bool> CanJoinAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken);
}
