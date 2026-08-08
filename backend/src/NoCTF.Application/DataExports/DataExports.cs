using NoCTF.Domain.DataExports;

namespace NoCTF.Application.DataExports;

public sealed record DataExportView(
    Guid Id,
    DataExportScope Scope,
    Guid? CompetitionId,
    Guid RequestedByUserId,
    DateTimeOffset RequestedAt,
    bool IncludeProtectedFlags,
    string? Reason,
    DataExportStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    Guid? FileId,
    string? FileName,
    string? ContentType,
    long? ByteLength,
    string? Sha256,
    DataExportFailureCode? FailureCode,
    string? FailureDetail);

public sealed record RequestDataExportCommand(
    DataExportScope Scope,
    Guid? CompetitionId,
    Guid RequestedByUserId,
    bool RequesterIsAdministrator,
    bool RequesterIsHuman,
    bool IncludeProtectedFlags,
    string? Reason);

public enum RequestDataExportFailure : short
{
    InvalidScope,
    SubjectNotFound,
    Forbidden,
    ProtectedFlagsRequireAdministrator,
    ProtectedFlagsRequireHuman,
    ReasonRequired,
    ActiveExportExists
}

public sealed record RequestDataExportResult(
    DataExportView? Export = null,
    RequestDataExportFailure? Failure = null);

public sealed record ListDataExportsQuery(
    DataExportScope Scope,
    Guid? CompetitionId,
    Guid RequesterId,
    bool RequesterIsAdministrator);

public enum ListDataExportsFailure : short
{
    SubjectNotFound,
    Forbidden
}

public sealed record ListDataExportsResult(
    IReadOnlyList<DataExportView>? Items = null,
    ListDataExportsFailure? Failure = null);

public sealed record AccessDataExportQuery(
    Guid DataExportId,
    Guid RequesterId,
    bool RequesterIsAdministrator);

public sealed record DataExportDownload(
    Stream Content,
    string FileName,
    string ContentType,
    long Length,
    string Sha256);

public enum AccessDataExportFailure : short
{
    NotFound,
    Forbidden,
    NotReady,
    Expired,
    ObjectMissing
}

public sealed record AccessDataExportResult(
    DataExportDownload? Download = null,
    AccessDataExportFailure? Failure = null);

public interface IDataExportStore
{
    Task<RequestDataExportResult> RequestAsync(
        RequestDataExportCommand command,
        CancellationToken cancellationToken);

    Task<ListDataExportsResult> ListAsync(
        ListDataExportsQuery query,
        CancellationToken cancellationToken);

    Task<AccessDataExportResult> AccessAsync(
        AccessDataExportQuery query,
        CancellationToken cancellationToken);
}

public interface IDataExportProcessor
{
    Task GenerateAsync(Guid dataExportId, CancellationToken cancellationToken);
    Task ExpireAsync(Guid dataExportId, CancellationToken cancellationToken);
    Task PurgeAsync(Guid dataExportId, CancellationToken cancellationToken);
}

public sealed class RequestDataExport(IDataExportStore store)
{
    public Task<RequestDataExportResult> ExecuteAsync(
        RequestDataExportCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalized = command with { Reason = command.Reason?.Trim() };
        if (normalized.Scope == DataExportScope.CompetitionArchive
            && normalized.CompetitionId is null
            || normalized.Scope == DataExportScope.PlatformAudit
            && normalized.CompetitionId is not null
            || normalized.Scope == DataExportScope.PlatformAudit
            && normalized.IncludeProtectedFlags)
        {
            return Task.FromResult(new RequestDataExportResult(
                Failure: RequestDataExportFailure.InvalidScope));
        }

        if (normalized.IncludeProtectedFlags && !normalized.RequesterIsAdministrator)
        {
            return Task.FromResult(new RequestDataExportResult(
                Failure: RequestDataExportFailure.ProtectedFlagsRequireAdministrator));
        }
        if (normalized.IncludeProtectedFlags && !normalized.RequesterIsHuman)
        {
            return Task.FromResult(new RequestDataExportResult(
                Failure: RequestDataExportFailure.ProtectedFlagsRequireHuman));
        }
        if (normalized.IncludeProtectedFlags
            && (normalized.Reason is null or { Length: < 8 or > 512 }))
        {
            return Task.FromResult(new RequestDataExportResult(
                Failure: RequestDataExportFailure.ReasonRequired));
        }

        return store.RequestAsync(normalized, cancellationToken);
    }
}

public sealed class ListDataExports(IDataExportStore store)
{
    public Task<ListDataExportsResult> ExecuteAsync(
        ListDataExportsQuery query,
        CancellationToken cancellationToken = default) =>
        store.ListAsync(query, cancellationToken);
}

public sealed class AccessDataExport(IDataExportStore store)
{
    public Task<AccessDataExportResult> ExecuteAsync(
        AccessDataExportQuery query,
        CancellationToken cancellationToken = default) =>
        store.AccessAsync(query, cancellationToken);
}

public sealed class ProcessDataExport(IDataExportProcessor processor)
{
    public Task GenerateAsync(
        Guid dataExportId,
        CancellationToken cancellationToken = default) =>
        processor.GenerateAsync(dataExportId, cancellationToken);

    public Task ExpireAsync(
        Guid dataExportId,
        CancellationToken cancellationToken = default) =>
        processor.ExpireAsync(dataExportId, cancellationToken);

    public Task PurgeAsync(
        Guid dataExportId,
        CancellationToken cancellationToken = default) =>
        processor.PurgeAsync(dataExportId, cancellationToken);
}
