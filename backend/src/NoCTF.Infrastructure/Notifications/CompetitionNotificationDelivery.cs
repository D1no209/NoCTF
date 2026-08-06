using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class CompetitionNotificationDelivery(
    NoCtfDbContext db,
    CompetitionNotificationAudienceResolver audiences)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task DeliverAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        Guid? requiredTeamId,
        CancellationToken ct)
    {
        var recipients = await audiences.ResolveAsync(
            competitionId,
            requiredTeamId,
            ct);
        if (recipients.Count == 0)
            return;

        await PersistAsync(
            competitionId,
            entityId,
            kind,
            sourceEventKey,
            JsonSerializer.Serialize(payload, JsonOptions),
            recipients,
            ct);
    }

    public async Task DeliverToUsersAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct)
    {
        if (recipientUserIds.Count == 0)
            return;
        var recipients = await db.Users.AsNoTracking()
            .Where(user => recipientUserIds.Contains(user.Id)
                && user.Kind == NoCTF.Domain.Identity.UserKind.Human
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        if (recipients.Length == 0)
            return;

        await PersistAsync(
            competitionId,
            entityId,
            kind,
            sourceEventKey,
            JsonSerializer.Serialize(payload, JsonOptions),
            recipients,
            ct);
    }

    private async Task PersistAsync(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        string payloadJson,
        IReadOnlyCollection<Guid> recipients,
        CancellationToken ct)
    {
        var existing = await db.Notifications.AsNoTracking()
            .Where(notification => recipients.Contains(notification.UserId)
                && notification.SourceEventKey == sourceEventKey)
            .Select(notification => notification.UserId)
            .ToArrayAsync(ct);
        var missing = recipients.Except(existing).Order().ToArray();
        if (missing.Length == 0)
            return;

        var createdAt = DateTimeOffset.UtcNow;
        Add(missing, createdAt);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var persisted = await db.Notifications.AsNoTracking()
                .Where(notification => missing.Contains(notification.UserId)
                    && notification.SourceEventKey == sourceEventKey)
                .Select(notification => notification.UserId)
                .ToArrayAsync(ct);
            var retryMissing = missing.Except(persisted).Order().ToArray();
            if (retryMissing.Length == 0)
                return;

            Add(retryMissing, DateTimeOffset.UtcNow);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                var nowPersisted = await db.Notifications.AsNoTracking()
                    .CountAsync(notification => retryMissing.Contains(notification.UserId)
                        && notification.SourceEventKey == sourceEventKey, ct);
                if (nowPersisted != retryMissing.Length)
                    throw;
            }
        }

        void Add(IEnumerable<Guid> userIds, DateTimeOffset timestamp) =>
            db.Notifications.AddRange(userIds.Select(userId => new Notification
            {
                Id = Guid.CreateVersion7(timestamp),
                UserId = userId,
                CompetitionId = competitionId,
                EntityId = entityId,
                Kind = kind,
                SourceEventKey = sourceEventKey,
                PayloadJson = payloadJson,
                CreatedAt = timestamp
            }));
    }
}
