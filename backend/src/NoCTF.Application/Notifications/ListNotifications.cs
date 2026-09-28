using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;

namespace NoCTF.Application.Notifications;

public sealed record NotificationView(
    Guid Id,
    NotificationSourceType SourceType,
    Guid? SourceId,
    NotificationTargetType TargetType,
    Guid TargetId,
    NotificationKind Kind,
    NotificationContent Content,
    EntityReferenceKind? RelatedType,
    Guid? RelatedId,
    Guid? ThreadRootId,
    Guid? ReplyToId,
    DateTimeOffset SentAt,
    string? SourceDisplayName = null);

public enum NotificationReadScope : short
{
    All,
    Inbox
}

public interface INotificationReader
{
    async Task<NotificationListPage> ListPageAsync(
        Guid userId,
        Guid? competitionId,
        int offset,
        int limit,
        bool desc,
        NotificationReadScope scope,
        CancellationToken cancellationToken)
    {
        var items = await (competitionId is { } id
            ? ListCompetitionAsync(userId, id, null, null, offset + limit, scope, cancellationToken)
            : ListAsync(userId, null, null, offset + limit, scope, cancellationToken));
        var ordered = desc ? items : items.Reverse().ToArray();
        return new NotificationListPage(ordered.Skip(offset).Take(limit).ToArray(), items.Count);
    }

    Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        NotificationReadScope scope,
        CancellationToken cancellationToken) =>
        ListAsync(userId, beforeCreatedAt, beforeId, limit, cancellationToken);

    Task<IReadOnlyList<NotificationView>> ListCompetitionAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken) =>
        ListAsync(
            userId,
            beforeCreatedAt,
            beforeId,
            limit,
            cancellationToken);

    Task<IReadOnlyList<NotificationView>> ListCompetitionAsync(
        Guid userId,
        Guid competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        NotificationReadScope scope,
        CancellationToken cancellationToken) =>
        ListCompetitionAsync(
            userId,
            competitionId,
            beforeCreatedAt,
            beforeId,
            limit,
            cancellationToken);

    Task<KeysetNotificationPosition> GetFeedCheckpointAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationView>> ReadFeedAsync(
        Guid userId,
        KeysetNotificationPosition position,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationView>?> ReadThreadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationView>?>(null);
}

public sealed record NotificationListPage(
    IReadOnlyList<NotificationView> Items,
    int Total);

public sealed record KeysetNotificationPosition(DateTimeOffset CreatedAt, Guid Id);

public sealed class ListNotifications(INotificationReader reader)
{
    public Task<NotificationListPage> ExecutePageAsync(
        Guid userId,
        Guid? competitionId,
        int offset,
        int limit,
        bool desc,
        NotificationReadScope scope,
        CancellationToken ct = default) =>
        reader.ListPageAsync(userId, competitionId, offset, limit, desc, scope, ct);

    public Task<IReadOnlyList<NotificationView>> ExecuteAsync(
        Guid userId,
        Guid? competitionId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        NotificationReadScope scope,
        CancellationToken ct = default) =>
        competitionId is { } id
            ? reader.ListCompetitionAsync(
                userId,
                id,
                beforeCreatedAt,
                beforeId,
                limit,
                scope,
                ct)
            : reader.ListAsync(userId, beforeCreatedAt, beforeId, limit, scope, ct);
}

public sealed class ReadNotificationFeed(INotificationReader reader)
{
    public Task<KeysetNotificationPosition> GetCheckpointAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct = default) =>
        reader.GetFeedCheckpointAsync(userId, now, ct);

    public Task<IReadOnlyList<NotificationView>> ExecuteAsync(
        Guid userId,
        KeysetNotificationPosition position,
        int limit,
        CancellationToken ct = default) =>
        reader.ReadFeedAsync(userId, position, limit, ct);
}
