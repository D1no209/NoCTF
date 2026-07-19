namespace NoCTF.Application.Submissions.Events;

/// <summary>The permanent input event for a Flag submission. The Flag is intentionally retained here only.</summary>
public sealed record FlagSubmissionReceived
{
    public required Guid SubmissionId { get; init; }
    public required Guid CompetitionId { get; init; }
    public required Guid TeamId { get; init; }
    public required Guid ChallengeId { get; init; }
    public required Guid UserId { get; init; }
    public required string Flag { get; init; }
    public required string IpAddress { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public string? ExpectedFlagAtReceipt { get; init; }

    public override string ToString() =>
        $"{nameof(FlagSubmissionReceived)} {{ SubmissionId = {SubmissionId}, Flag = [REDACTED] }}";
}
