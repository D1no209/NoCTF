namespace NoCTF.API.Endpoints.Teams;

public sealed class InviteTeamMemberRequest
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid InvitedUserId { get; set; }
}

public sealed record TeamInvitationResponse(Guid Id, Guid CompetitionId, Guid TeamId, Guid InvitedUserId, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt);
