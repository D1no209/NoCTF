namespace NoCTF.Domain.Notifications;

/// <summary>Represents a durable user notification.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CompetitionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
