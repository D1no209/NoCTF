using Microsoft.AspNetCore.Http;
using NoCTF.Core;

namespace NoCTF.Application.QqBot;

public sealed record QqBotGlobalSettingsView(
    bool Enabled,
    int LongPollSeconds,
    int DeliveryLeaseSeconds,
    int MaxDeliveryAttempts,
    int MaxMessageLength,
    int MaxPendingDeliveries,
    int GroupCooldownMilliseconds,
    int CompetitionCooldownMilliseconds,
    int ManualNotificationCooldownSeconds,
    DateTime UpdatedAt);

public sealed record QqBotGlobalSettingsUpdate(
    bool Enabled,
    int LongPollSeconds,
    int DeliveryLeaseSeconds,
    int MaxDeliveryAttempts,
    int MaxMessageLength,
    int MaxPendingDeliveries,
    int GroupCooldownMilliseconds,
    int CompetitionCooldownMilliseconds,
    int ManualNotificationCooldownSeconds);

public sealed record QqBotAgentView(
    Guid Id,
    string Name,
    bool Enabled,
    bool HasPublicKey,
    bool HasPreviousPublicKey,
    DateTime? PreviousKeyValidUntil,
    DateTime? LastHeartbeatAt,
    bool ApiReachable,
    bool QqOnline,
    long? BotUin,
    string? BotNickname,
    string? ImplementationName,
    string? ImplementationVersion,
    string? MilkyVersion,
    string? LastErrorCode,
    string? LastErrorSummary,
    int PendingDeliveries,
    int RecentSucceeded,
    int RecentFailed,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record QqBotAgentUpdate(
    Guid? Id,
    string Name,
    bool Enabled,
    string PublicKeyPem,
    int PreviousKeyOverlapMinutes = 60);

public sealed record QqBotGroupView(
    Guid Id,
    Guid AgentId,
    long GroupId,
    string GroupName,
    bool IsPresent,
    bool IsAuthorized,
    DateTime LastSeenAt);

public sealed record QqBotGroupAuthorizationUpdate(bool IsAuthorized);

public sealed record QqBotAdminOverview(
    bool PluginAvailable,
    string ConnectionType,
    QqBotGlobalSettingsView Settings,
    IReadOnlyList<QqBotAgentView> Agents,
    IReadOnlyList<QqBotGroupView> Groups);

public sealed record QqBotEventRuleView(
    QqBotEventType EventType,
    bool Enabled,
    Guid? TemplateId);

public sealed record QqBotGroupBindingView(
    Guid? Id,
    Guid AgentId,
    Guid GroupId,
    long QqGroupId,
    string GroupName,
    bool IsDefault,
    IReadOnlyList<QqBotEventType> EventTypes);

public sealed record CompetitionQqBotConfigurationView(
    Guid CompetitionId,
    bool GlobalEnabled,
    bool HasOnlineAgent,
    bool Enabled,
    bool AllowMessages,
    bool AllowManualNotifications,
    bool StopNormalEventsAfterFinished,
    bool MentionAll,
    bool ShowTeamName,
    bool ShowUserName,
    bool ShowChallengeCategory,
    bool IncludeCompetitionLink,
    bool IncludeChallengeLink,
    bool HidePenaltyDetails,
    IReadOnlyList<QqBotEventRuleView> EventRules,
    IReadOnlyList<QqBotGroupBindingView> GroupBindings,
    IReadOnlyList<string> Warnings,
    DateTime? UpdatedAt);

public sealed record CompetitionQqBotConfigurationUpdate(
    bool Enabled,
    bool AllowMessages,
    bool AllowManualNotifications,
    bool StopNormalEventsAfterFinished,
    bool MentionAll,
    bool ShowTeamName,
    bool ShowUserName,
    bool ShowChallengeCategory,
    bool IncludeCompetitionLink,
    bool IncludeChallengeLink,
    bool HidePenaltyDetails,
    IReadOnlyList<QqBotEventRuleView> EventRules,
    IReadOnlyList<QqBotGroupBindingView> GroupBindings);

public sealed record QqBotTemplateView(
    Guid? Id,
    Guid? CompetitionId,
    QqBotEventType EventType,
    string Name,
    string Content,
    bool IsDefault,
    bool IsBuiltIn,
    IReadOnlyList<string> AllowedVariables,
    DateTime? UpdatedAt);

public sealed record QqBotTemplateUpdate(
    Guid? Id,
    Guid? CompetitionId,
    QqBotEventType EventType,
    string Name,
    string Content,
    bool IsDefault);

public sealed record QqBotPreviewRequest(
    Guid CompetitionId,
    QqBotEventType EventType,
    Guid? TemplateId,
    string? AnnouncementTitle,
    string? AnnouncementContent);

public sealed record QqBotPreviewResult(
    string RenderedText,
    string SegmentsJson,
    int CharacterCount,
    IReadOnlyList<string> Warnings);

public sealed record QqBotManualNotificationRequest(
    Guid CompetitionId,
    string Title,
    string Content,
    IReadOnlyList<Guid> GroupIds,
    Guid? TemplateId,
    bool IsTest);

public sealed record QqBotQueuedNotification(Guid EventId, int TargetGroupCount, string Status);

public sealed record QqBotDeliveryLogFilter(
    Guid? CompetitionId,
    QqBotEventType? EventType,
    QqBotDeliveryStatus? Status,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);

public sealed record QqBotDeliveryLogView(
    Guid Id,
    Guid CompetitionId,
    string CompetitionName,
    Guid EventId,
    QqBotEventType EventType,
    QqBotDeliverySource Source,
    long QqGroupId,
    string GroupName,
    QqBotDeliveryStatus Status,
    int AttemptCount,
    int MaxAttempts,
    string MessageSummary,
    string MessageDigest,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? SentAt,
    string? LastErrorCode,
    string? LastErrorSummary,
    long? RemoteMessageSequence,
    DateTime? RemoteSentAt,
    Guid? RetriedByUserId);

public sealed record QqBotDeliveryLogPage(
    IReadOnlyList<QqBotDeliveryLogView> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record QqBotRetryResult(Guid EventId, Guid ParentDeliveryId, string Status);

public interface IQqBotAdministrationService
{
    bool IsAvailable { get; }
    Task<QqBotAdminOverview> GetOverviewAsync(CancellationToken ct = default);
    Task<QqBotGlobalSettingsView> UpdateGlobalSettingsAsync(QqBotGlobalSettingsUpdate update, CancellationToken ct = default);
    Task<QqBotAgentView> UpsertAgentAsync(QqBotAgentUpdate update, CancellationToken ct = default);
    Task<QqBotGroupView> SetGroupAuthorizationAsync(Guid groupId, bool isAuthorized, CancellationToken ct = default);
    Task<CompetitionQqBotConfigurationView> GetCompetitionConfigurationAsync(Guid competitionId, CancellationToken ct = default);
    Task<CompetitionQqBotConfigurationView> UpdateCompetitionConfigurationAsync(Guid competitionId, CompetitionQqBotConfigurationUpdate update, CancellationToken ct = default);
    Task<IReadOnlyList<QqBotTemplateView>> GetTemplatesAsync(Guid? competitionId, CancellationToken ct = default);
    Task<QqBotTemplateView> UpsertTemplateAsync(QqBotTemplateUpdate update, Guid actorUserId, CancellationToken ct = default);
    Task<QqBotPreviewResult> PreviewAsync(QqBotPreviewRequest request, CancellationToken ct = default);
    Task<QqBotQueuedNotification> QueueManualNotificationAsync(QqBotManualNotificationRequest request, Guid actorUserId, CancellationToken ct = default);
    Task<QqBotDeliveryLogPage> GetDeliveryLogsAsync(QqBotDeliveryLogFilter filter, CancellationToken ct = default);
    Task<QqBotRetryResult> RetryDeliveryAsync(Guid deliveryId, Guid actorUserId, CancellationToken ct = default);
}

public sealed record QqBotAgentAuthenticationResult(bool Succeeded, Guid AgentId, string? ErrorCode)
{
    public static QqBotAgentAuthenticationResult Failure(string errorCode) => new(false, Guid.Empty, errorCode);
    public static QqBotAgentAuthenticationResult Success(Guid agentId) => new(true, agentId, null);
}

public sealed record QqBotHeartbeatRequest(
    bool QqOnline,
    long? BotUin,
    string? BotNickname,
    string? ImplementationName,
    string? ImplementationVersion,
    string? MilkyVersion,
    string? ErrorCode,
    string? ErrorSummary);

public sealed record QqBotGroupSyncItem(long GroupId, string GroupName);
public sealed record QqBotGroupSyncRequest(IReadOnlyList<QqBotGroupSyncItem> Groups);
public sealed record QqBotGroupSyncResult(int SeenGroups, int AuthorizedGroups);

public sealed record QqBotLeaseRequest(int WaitSeconds = 25);
public sealed record QqBotDeliveryLease(
    Guid DeliveryId,
    Guid LeaseToken,
    long GroupId,
    string SegmentsJson,
    DateTime LockedUntil,
    int Attempt,
    int MaxAttempts);

public sealed record QqBotLeaseResponse(QqBotDeliveryLease? Delivery, int RetryAfterMilliseconds);

public sealed record QqBotDeliveryAckRequest(
    Guid DeliveryId,
    Guid LeaseToken,
    long? MessageSequence,
    DateTime? RemoteSentAt,
    string? SafeRemoteSummary);

public sealed record QqBotDeliveryFailureRequest(
    Guid DeliveryId,
    Guid LeaseToken,
    string ErrorCode,
    string? SafeSummary);

public sealed record QqBotAgentOperationResult(bool Succeeded, string Status, string? ErrorCode = null);

public interface IQqBotAgentService
{
    bool IsAvailable { get; }
    Task<QqBotAgentAuthenticationResult> AuthenticateAsync(HttpContext httpContext, ReadOnlyMemory<byte> body, CancellationToken ct = default);
    Task<QqBotAgentOperationResult> HeartbeatAsync(Guid agentId, QqBotHeartbeatRequest request, CancellationToken ct = default);
    Task<QqBotGroupSyncResult> SyncGroupsAsync(Guid agentId, QqBotGroupSyncRequest request, CancellationToken ct = default);
    Task<QqBotLeaseResponse> LeaseAsync(Guid agentId, QqBotLeaseRequest request, CancellationToken ct = default);
    Task<QqBotAgentOperationResult> AcknowledgeAsync(Guid agentId, QqBotDeliveryAckRequest request, CancellationToken ct = default);
    Task<QqBotAgentOperationResult> FailAsync(Guid agentId, QqBotDeliveryFailureRequest request, CancellationToken ct = default);
}

public sealed class UnavailableQqBotService : IQqBotAdministrationService, IQqBotAgentService
{
    public bool IsAvailable => false;

    public Task<QqBotAdminOverview> GetOverviewAsync(CancellationToken ct = default) => Unavailable<QqBotAdminOverview>();
    public Task<QqBotGlobalSettingsView> UpdateGlobalSettingsAsync(QqBotGlobalSettingsUpdate update, CancellationToken ct = default) => Unavailable<QqBotGlobalSettingsView>();
    public Task<QqBotAgentView> UpsertAgentAsync(QqBotAgentUpdate update, CancellationToken ct = default) => Unavailable<QqBotAgentView>();
    public Task<QqBotGroupView> SetGroupAuthorizationAsync(Guid groupId, bool isAuthorized, CancellationToken ct = default) => Unavailable<QqBotGroupView>();
    public Task<CompetitionQqBotConfigurationView> GetCompetitionConfigurationAsync(Guid competitionId, CancellationToken ct = default) => Unavailable<CompetitionQqBotConfigurationView>();
    public Task<CompetitionQqBotConfigurationView> UpdateCompetitionConfigurationAsync(Guid competitionId, CompetitionQqBotConfigurationUpdate update, CancellationToken ct = default) => Unavailable<CompetitionQqBotConfigurationView>();
    public Task<IReadOnlyList<QqBotTemplateView>> GetTemplatesAsync(Guid? competitionId, CancellationToken ct = default) => Unavailable<IReadOnlyList<QqBotTemplateView>>();
    public Task<QqBotTemplateView> UpsertTemplateAsync(QqBotTemplateUpdate update, Guid actorUserId, CancellationToken ct = default) => Unavailable<QqBotTemplateView>();
    public Task<QqBotPreviewResult> PreviewAsync(QqBotPreviewRequest request, CancellationToken ct = default) => Unavailable<QqBotPreviewResult>();
    public Task<QqBotQueuedNotification> QueueManualNotificationAsync(QqBotManualNotificationRequest request, Guid actorUserId, CancellationToken ct = default) => Unavailable<QqBotQueuedNotification>();
    public Task<QqBotDeliveryLogPage> GetDeliveryLogsAsync(QqBotDeliveryLogFilter filter, CancellationToken ct = default) => Unavailable<QqBotDeliveryLogPage>();
    public Task<QqBotRetryResult> RetryDeliveryAsync(Guid deliveryId, Guid actorUserId, CancellationToken ct = default) => Unavailable<QqBotRetryResult>();
    public Task<QqBotAgentAuthenticationResult> AuthenticateAsync(HttpContext httpContext, ReadOnlyMemory<byte> body, CancellationToken ct = default) => Task.FromResult(QqBotAgentAuthenticationResult.Failure("plugin_unavailable"));
    public Task<QqBotAgentOperationResult> HeartbeatAsync(Guid agentId, QqBotHeartbeatRequest request, CancellationToken ct = default) => Unavailable<QqBotAgentOperationResult>();
    public Task<QqBotGroupSyncResult> SyncGroupsAsync(Guid agentId, QqBotGroupSyncRequest request, CancellationToken ct = default) => Unavailable<QqBotGroupSyncResult>();
    public Task<QqBotLeaseResponse> LeaseAsync(Guid agentId, QqBotLeaseRequest request, CancellationToken ct = default) => Unavailable<QqBotLeaseResponse>();
    public Task<QqBotAgentOperationResult> AcknowledgeAsync(Guid agentId, QqBotDeliveryAckRequest request, CancellationToken ct = default) => Unavailable<QqBotAgentOperationResult>();
    public Task<QqBotAgentOperationResult> FailAsync(Guid agentId, QqBotDeliveryFailureRequest request, CancellationToken ct = default) => Unavailable<QqBotAgentOperationResult>();

    private static Task<T> Unavailable<T>() => Task.FromException<T>(new InvalidOperationException("QQBot plugin is unavailable."));
}
