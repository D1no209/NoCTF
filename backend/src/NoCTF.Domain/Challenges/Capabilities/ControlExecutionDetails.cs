using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Challenges.Capabilities;

public sealed class ControlExecutionDetails
{
    public Guid ExecutionId { get; set; }
    public Guid RuntimeInstanceId { get; set; }
    [MaxLength(128)] public string RunnerId { get; set; } = string.Empty;
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? ObservedAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? WorkCompletedAt { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public ControlResult? Result { get; set; }
    [MaxLength(4096)] public string? ControllerIdentifier { get; set; }
    public CapabilityExecutionFailure? Failure { get; set; }
    public CapabilityExecutionFailure? ReportedFailure { get; set; }
    public override string ToString() => $"ControlExecutionDetails {{ ExecutionId = {ExecutionId}, RuntimeInstanceId = {RuntimeInstanceId}, Result = {Result}, Failure = {Failure} }}";
}
