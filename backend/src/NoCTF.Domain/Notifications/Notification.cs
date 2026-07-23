namespace NoCTF.Domain.Notifications;

/// <summary>Represents a durable user notification.</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? EntityId { get; set; }
    public NotificationKind Kind { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public enum NotificationKind : short
{
    CompetitionLifecycleChanged,
    TeamRegistrationChanged,
    SubmissionEvaluated,
    RuntimeStateChanged,
    StartGateFailed
}
