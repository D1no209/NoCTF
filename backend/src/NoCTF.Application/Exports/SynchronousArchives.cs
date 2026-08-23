using NoCTF.Application.Administration.PlatformLogs;

namespace NoCTF.Application.Exports;

public enum SynchronousArchiveFailure : short
{
    SubjectNotFound,
    Forbidden,
    ProtectedFlagsRequireAdministrator,
    ProtectedFlagsRequireHuman,
    ReasonRequired,
    InvalidQuery,
    RecordLimitExceeded,
    CompressedSizeLimitExceeded,
    TimeLimitExceeded,
    MemoryLimitExceeded,
    GenerationFailed
}

public sealed record SynchronousArchive(
    Stream Content,
    string FileName,
    string ContentType);

public sealed record SynchronousArchiveResult(
    SynchronousArchive? Archive = null,
    SynchronousArchiveFailure? Failure = null);

public sealed record ExportCompetitionArchiveCommand(
    Guid CompetitionId,
    Guid RequestedByUserId,
    bool RequesterIsAdministrator,
    bool RequesterIsHuman,
    bool IncludeProtectedFlags,
    string? Reason);

public sealed record ExportPlatformAuditArchiveCommand(
    Guid RequestedByUserId,
    bool RequesterIsAdministrator,
    bool RequesterIsHuman,
    PlatformAuditKind? Kind,
    Guid? CompetitionId,
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To);

public sealed record PlatformAuditArchiveExportedFact(
    int SchemaVersion,
    PlatformAuditKind? Kind,
    Guid? CompetitionId,
    Guid? ActorId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long RecordCount);

public interface ISynchronousArchiveGenerator
{
    Task<SynchronousArchiveResult> GenerateCompetitionAsync(
        ExportCompetitionArchiveCommand command,
        CancellationToken cancellationToken);

    Task<SynchronousArchiveResult> GeneratePlatformAuditAsync(
        ExportPlatformAuditArchiveCommand command,
        CancellationToken cancellationToken);
}

public sealed class ExportCompetitionArchive(ISynchronousArchiveGenerator generator)
{
    public Task<SynchronousArchiveResult> ExecuteAsync(
        ExportCompetitionArchiveCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalized = command with { Reason = command.Reason?.Trim() };
        if (normalized.IncludeProtectedFlags && !normalized.RequesterIsAdministrator)
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.ProtectedFlagsRequireAdministrator));
        }
        if (normalized.IncludeProtectedFlags && !normalized.RequesterIsHuman)
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.ProtectedFlagsRequireHuman));
        }
        if (normalized.IncludeProtectedFlags
            && normalized.Reason is null or { Length: < 8 or > 512 })
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.ReasonRequired));
        }

        return generator.GenerateCompetitionAsync(normalized, cancellationToken);
    }
}

public sealed class ExportPlatformAuditArchive(ISynchronousArchiveGenerator generator)
{
    public Task<SynchronousArchiveResult> ExecuteAsync(
        ExportPlatformAuditArchiveCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!command.RequesterIsAdministrator || !command.RequesterIsHuman)
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.Forbidden));
        }
        if (command.From is DateTimeOffset from
            && command.To is DateTimeOffset to
            && from > to)
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.InvalidQuery));
        }
        if ((command.From is null) != (command.To is null))
        {
            return Task.FromResult(new SynchronousArchiveResult(
                Failure: SynchronousArchiveFailure.InvalidQuery));
        }

        return generator.GeneratePlatformAuditAsync(command, cancellationToken);
    }
}
