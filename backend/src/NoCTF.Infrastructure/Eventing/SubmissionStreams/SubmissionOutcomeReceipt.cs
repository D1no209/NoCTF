using NoCTF.Application.Submissions.Events;

namespace NoCTF.Infrastructure.Eventing.SubmissionStreams;

/// <summary>Unique receipt for the single outcome allowed for one submission.</summary>
public sealed class SubmissionOutcomeReceipt
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid SubmissionId { get; set; }
    public SubmissionOutcome Outcome { get; set; }
    public long StreamSequence { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

/// <summary>Deterministic receipt for platform input idempotency.</summary>
public sealed class CompetitionInputReceipt
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset RecordedAt { get; set; }
}
