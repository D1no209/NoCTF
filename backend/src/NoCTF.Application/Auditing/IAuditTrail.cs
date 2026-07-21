namespace NoCTF.Application.Auditing;

public sealed record AuditRecord(
    Guid? CompetitionId,
    Guid? ActorId,
    string Action,
    string SubjectType,
    Guid? SubjectId,
    IReadOnlyDictionary<string, object?> Metadata,
    DateTimeOffset OccurredAt,
    string? IpAddress);

public interface IAuditTrail
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken);
}
