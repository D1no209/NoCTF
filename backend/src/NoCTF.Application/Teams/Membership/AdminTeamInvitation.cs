namespace NoCTF.Application.Teams.Membership;

public interface IAdminTeamInvitationReader
{
    Task<string?> ReadAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken);
}

public sealed class GetAdminTeamInvitation(IAdminTeamInvitationReader reader)
{
    public Task<string?> ExecuteAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(competitionId, teamId, cancellationToken);
}
