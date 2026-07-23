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
}

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
