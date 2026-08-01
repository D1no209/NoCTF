using NoCTF.Domain.Notifications;

namespace NoCTF.Application.Notifications;

public sealed record NotificationView(
    Guid Id,
    Guid? CompetitionId,
    Guid? EntityId,
    NotificationKind Kind,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public interface INotificationReader
{
    Task<IReadOnlyList<NotificationView>> ListAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);

    Task<KeysetNotificationPosition> GetFeedCheckpointAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationView>> ReadFeedAsync(
        Guid userId,
        KeysetNotificationPosition position,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record KeysetNotificationPosition(DateTimeOffset CreatedAt, Guid Id);

public sealed class ListNotifications(INotificationReader reader)
{
    public Task<IReadOnlyList<NotificationView>> ExecuteAsync(
        Guid userId,
        DateTimeOffset? beforeCreatedAt,
        Guid? beforeId,
        int limit,
        CancellationToken ct = default) =>
        reader.ListAsync(userId, beforeCreatedAt, beforeId, limit, ct);
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
