using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Administration.PlatformLogs;

public enum PlatformLogService : short
{
    Api,
    Worker,
    Runner,
    Host
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
    Guid? RuntimeInstanceId,
    Guid? TeamId,
    Guid? UserId,
    Guid? CompetitionChallengeId,
    Guid? GameplayFactId);

public sealed record PlatformLogQuery(
    PlatformLogLevel MinimumLevel,
    PlatformLogService? Service,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Category,
    string? Search,
    Guid? CompetitionId,
    Guid? RuntimeInstanceId,
    Guid? TeamId,
    Guid? UserId,
    Guid? CompetitionChallengeId,
    Guid? GameplayFactId,
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

    Task<PlatformLogExportResult> ExportAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken);
}

public sealed record PlatformLogExport(Stream Content, string FileName);

public sealed record PlatformLogExportResult(
    PlatformLogReadState State,
    PlatformLogExport? Export = null);

public enum PlatformAuditKind : short
{
    CompetitionLifecycle,
    UserAccountLifecycle,
    PlatformAdministration,
    CompetitionAdministration,
    CompetitionLeaderboardVisibility,
    CompetitionEvent
}

public enum PlatformAdministrationAction : short
{
    AuditArchiveExported,
    UserAccessTokenIssued,
    UserAccessTokenRevoked,
    UserTokensInvalidated
}

public sealed record PlatformAuditView(
    Guid Id,
    PlatformAuditKind Kind,
    Guid SubjectId,
    Guid? CompetitionId,
    Guid? ActorId,
    CompetitionStatus? FromCompetitionStatus,
    CompetitionStatus? ToCompetitionStatus,
    CompetitionLeaderboardVisibility? FromLeaderboardVisibility,
    CompetitionLeaderboardVisibility? ToLeaderboardVisibility,
    UserAccountLifecycleAction? UserAccountAction,
    PlatformAdministrationAction? PlatformAdministrationAction,
    CompetitionEventKind? CompetitionEventKind,
    CompetitionEventLevel? CompetitionEventLevel,
    CompetitionEventVisibility? CompetitionEventVisibility,
    Guid? RelatedUserId,
    Guid? TeamId,
    Guid? CompetitionChallengeId,
    Guid? RuntimeInstanceId,
    Guid? GameplayFactId,
    Guid? QuestionId,
    GameplayFactKind? GameplayFactKind,
    GameplayFactState? GameplayFactState,
    GameplayFactResult? GameplayFactResult,
    string? SubjectDisplayName,
    string? Reason,
    bool Automatic,
    DateTimeOffset OccurredAt,
    Guid? FileId = null,
    Guid? JwtId = null,
    DateTimeOffset? TokenExpiresAt = null,
    int? TokenVersion = null);

public sealed record PlatformAuditQuery(
    PlatformAuditKind? Kind,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? CompetitionId,
    Guid? ActorId,
    DateTimeOffset? BeforeOccurredAt,
    Guid? BeforeId,
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

public sealed class ExportPlatformLogs(IPlatformLogReader logs)
{
    public Task<PlatformLogExportResult> ExecuteAsync(
        PlatformLogQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.From is null
            || query.To is null
            || query.From > query.To
            || query.To - query.From > TimeSpan.FromDays(14)
            || query.Limit != 50_000
            || query.Cursor is not null)
        {
            return Task.FromResult(new PlatformLogExportResult(
                PlatformLogReadState.Unavailable));
        }
        return logs.ExportAsync(query, cancellationToken);
    }
}
