namespace NoCTF.Domain.Auditing;

/// <summary>Represents an immutable security or administration audit record.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid? CompetitionId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public string? IpAddress { get; set; }
}
