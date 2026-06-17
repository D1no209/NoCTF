namespace NoCTF.API.SignalR;

/// <summary>
/// Strongly-typed client interface for organizer/admin monitoring.
/// </summary>
public interface IMonitorClient
{
    /// <summary>Sent when a submission is processed (any team).</summary>
    Task ReceiveSubmissionEvent(SubmissionEventDto submission);

    /// <summary>Sent when a container lifecycle event occurs.</summary>
    Task ReceiveContainerEvent(ContainerEventDto container);

    /// <summary>Sent for system-level alerts (e.g., scoring errors, infra issues).</summary>
    Task ReceiveSystemAlert(SystemAlertDto alert);

    /// <summary>Sent when a new log entry is available (admin log streaming).</summary>
    Task ReceiveLogEntry(LogEntryDto entry);
}

public record SubmissionEventDto(
    Guid SubmissionId,
    Guid TeamId,
    string TeamName,
    Guid ChallengeId,
    string ChallengeName,
    bool IsCorrect,
    DateTimeOffset SubmittedAt);

public record ContainerEventDto(
    Guid ContainerId,
    Guid ChallengeId,
    Guid TeamId,
    string Event,    // "started" | "stopped" | "error"
    DateTimeOffset Timestamp);

public record SystemAlertDto(
    string Level,    // "info" | "warning" | "error"
    string Message,
    DateTimeOffset Timestamp);

public record LogEntryDto(
    string Level,
    string Message,
    string Source,
    DateTimeOffset Timestamp);
