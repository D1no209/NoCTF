namespace NoCTF.Application.Messaging;

public sealed record CleanupObject(
    Guid ObjectReferenceId,
    long ProcessingVersion);
