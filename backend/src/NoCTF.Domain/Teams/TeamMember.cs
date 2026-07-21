namespace NoCTF.Domain.Teams;

/// <summary>Represents membership of a user in a competition team.</summary>
public sealed class TeamMember
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int MemberOrder { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}
