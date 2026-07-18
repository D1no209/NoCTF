namespace NoCTF.Domain.Teams;

/// <summary>Stores the current relational ban ruling for a team.</summary>
public sealed class TeamBanState
{
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public Guid? BannedById { get; set; }
    public string? Reason { get; set; }
}
