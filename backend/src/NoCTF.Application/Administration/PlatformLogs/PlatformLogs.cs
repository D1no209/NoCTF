using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Application.Administration.PlatformLogs;

public enum PlatformLogService : short
{
    Api,
    Worker,
    Runner
}

public enum PlatformLogLevel : short
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record PlatformLogView(
    string Cursor,
    DateTimeOffset Timestamp,
    PlatformLogService Service,
    PlatformLogLevel Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage,
    Guid? CompetitionId,
    Guid? RuntimeInstanceId);

public sealed record PlatformLogQuery(
    PlatformLogLevel MinimumLevel,
    PlatformLogService? Service,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? CompetitionId,
    Guid? RuntimeInstanceId,
    string? Cursor,
    int Limit);

public enum PlatformLogReadState
{
    Available,
    Unavailable
}

public sealed record PlatformLogQueryResult(
    PlatformLogReadState State,
    IReadOnlyList<PlatformLogView> Items,
    string? NextCursor = null);

public interface IPlatformLogReader
{
    Task<PlatformLogQueryResult> QueryAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken);
}

public enum PlatformAuditKind : short
{
    CompetitionLifecycle,
    UserAccountLifecycle
}

public sealed record PlatformAuditView(
    Guid Id,
    PlatformAuditKind Kind,
    Guid SubjectId,
    Guid? CompetitionId,
    Guid? ActorId,
    CompetitionStatus? FromCompetitionStatus,
    CompetitionStatus? ToCompetitionStatus,
    UserAccountLifecycleAction? UserAccountAction,
    string? SubjectDisplayName,
    string? Reason,
    bool Automatic,
    DateTimeOffset OccurredAt);

public sealed record PlatformAuditQuery(
    PlatformAuditKind? Kind,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? CompetitionId,
    Guid? ActorId,
    int Limit);

public interface IPlatformAuditLogStore
{
    Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken);
}

public sealed class ObservePlatform(
    IPlatformLogReader logs,
    IPlatformAuditLogStore audits)
{
    public Task<PlatformLogQueryResult> QueryLogsAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken = default) =>
        logs.QueryAsync(query, cancellationToken);

    public Task<IReadOnlyList<PlatformAuditView>> QueryAuditsAsync(
        PlatformAuditQuery query,
        CancellationToken cancellationToken = default) =>
        audits.QueryAsync(query, cancellationToken);
}
