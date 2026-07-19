namespace NoCTF.Domain.Teams;

public enum TeamInvitationStatus
{
    Pending,
    Accepted,
    Rejected
}

public sealed class TeamInvitation
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid InvitedUserId { get; set; }
    public Guid InvitedById { get; set; }
    public TeamInvitationStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
