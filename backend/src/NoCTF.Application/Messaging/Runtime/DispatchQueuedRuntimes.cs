namespace NoCTF.Application.Messaging;

/// <summary>A bounded traversal of queued facts; no business scheduling cursor is persisted.</summary>
public sealed record DispatchQueuedRuntimes(
    DateTimeOffset At,
    DateTimeOffset? AfterCreatedAt = null,
    Guid? AfterId = null);
