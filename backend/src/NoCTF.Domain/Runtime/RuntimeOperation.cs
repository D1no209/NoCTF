namespace NoCTF.Domain.Runtime;

/// <summary>Tracks an idempotent runtime operation.</summary>
public sealed class RuntimeOperation
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? ChallengeInstanceId { get; set; }
    public string OperationKey { get; set; } = string.Empty;
    public RuntimeOperationKind Kind { get; set; }
    public RuntimeStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
