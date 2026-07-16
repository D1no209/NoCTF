using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Events;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.Application.Notifications;

public sealed record UserNotificationView(
    Guid Id,
    Guid? CompetitionId,
    Guid? SubjectId,
    string Type,
    IReadOnlyDictionary<string, string> Data,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt);

public sealed record UserNotificationPage(
    IReadOnlyList<UserNotificationView> Items,
    int UnreadCount);

public interface IUserNotificationService
{
    Task<UserNotificationPage> GetAsync(Guid userId, int limit, CancellationToken ct = default);
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}

public sealed class UserNotificationService(ApplicationDbContext db) : IUserNotificationService
{
    public async Task<UserNotificationPage> GetAsync(Guid userId, int limit, CancellationToken ct = default)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 50);
        var items = await db.UserNotifications
            .AsNoTracking()
            .Where(notification => notification.UserId == userId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(effectiveLimit)
            .ToListAsync(ct);
        var unreadCount = await db.UserNotifications
            .CountAsync(notification => notification.UserId == userId && !notification.IsRead, ct);

        return new UserNotificationPage(items.Select(ToView).ToList(), unreadCount);
    }

    public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notification = await db.UserNotifications.FirstOrDefaultAsync(
            item => item.Id == notificationId && item.UserId == userId,
            ct);
        if (notification is null)
            return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var notifications = await db.UserNotifications
            .Where(notification => notification.UserId == userId && !notification.IsRead)
            .ToListAsync(ct);
        if (notifications.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }
        await db.SaveChangesAsync(ct);
        return notifications.Count;
    }

    private static UserNotificationView ToView(UserNotification notification)
        => new(
            notification.Id,
            notification.CompetitionId,
            notification.SubjectId,
            notification.Type,
            DeserializeData(notification.DataJson),
            notification.IsRead,
            notification.CreatedAt,
            notification.ReadAt);

    private static IReadOnlyDictionary<string, string> DeserializeData(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class UserNotificationOutbox(ApplicationDbContext db) : ICompetitionNotificationSink
{
    public void Add(CompetitionNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Source is "test" or "manual-retry")
            return;

        var data = BuildSafeData(notification);
        if (data is null)
            return;

        var recipientIds = FindRecipientIds(notification.CompetitionId);
        if (recipientIds.Count == 0)
            return;

        var trackedRecipientIds = db.ChangeTracker.Entries<UserNotification>()
            .Where(entry => entry.State != EntityState.Deleted &&
                entry.Entity.IdempotencyKey == notification.IdempotencyKey)
            .Select(entry => entry.Entity.UserId)
            .ToHashSet();
        var persistedRecipientIds = db.UserNotifications
            .AsNoTracking()
            .Where(item => item.IdempotencyKey == notification.IdempotencyKey && recipientIds.Contains(item.UserId))
            .Select(item => item.UserId)
            .ToHashSet();
        var dataJson = SerializeData(data);
        var now = DateTime.UtcNow;

        foreach (var recipientId in recipientIds)
        {
            if (trackedRecipientIds.Contains(recipientId) || persistedRecipientIds.Contains(recipientId))
                continue;

            db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(),
                UserId = recipientId,
                CompetitionId = notification.CompetitionId,
                SubjectId = notification.SubjectId,
                Type = notification.Type,
                DataJson = dataJson,
                IdempotencyKey = notification.IdempotencyKey,
                CreatedAt = now
            });
        }
    }

    private HashSet<Guid> FindRecipientIds(Guid competitionId)
    {
        var recipientIds = db.Competitions.IgnoreQueryFilters()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => competition.OwnerId)
            .ToHashSet();

        var trackedOwnerId = db.ChangeTracker.Entries<Competition>()
            .Where(entry => entry.State != EntityState.Deleted && entry.Entity.Id == competitionId)
            .Select(entry => entry.Entity.OwnerId)
            .FirstOrDefault();
        if (trackedOwnerId != Guid.Empty)
            recipientIds.Add(trackedOwnerId);

        recipientIds.UnionWith(db.CompetitionCollaborators
            .AsNoTracking()
            .Where(collaborator => collaborator.CompetitionId == competitionId)
            .Select(collaborator => collaborator.UserId));
        recipientIds.UnionWith(
            from member in db.TeamMembers.AsNoTracking()
            join team in db.Teams.IgnoreQueryFilters().AsNoTracking()
                on member.TeamId equals team.Id
            where member.CompetitionId == competitionId &&
                team.CompetitionId == competitionId &&
                team.RegistrationStatus == TeamRegistrationStatus.Approved
            select member.UserId);

        recipientIds.Remove(Guid.Empty);
        return recipientIds;
    }

    private static Dictionary<string, string>? BuildSafeData(CompetitionNotification notification)
    {
        Dictionary<string, string> payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    notification.PayloadJson,
                    JsonOptions)?
                .ToDictionary(pair => pair.Key, pair => ElementText(pair.Value), StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            payload = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string Value(string key, int maxLength)
        {
            var value = payload.GetValueOrDefault(key, string.Empty);
            return Limit(value, maxLength);
        }
        Dictionary<string, string> Select(params (string Key, int MaxLength)[] fields)
            => fields.ToDictionary(
                field => field.Key,
                field => Value(field.Key, field.MaxLength),
                StringComparer.Ordinal);

        return notification.Type switch
        {
            CompetitionNotificationTypes.CompetitionStarted => Select(("competition_name", 256)),
            CompetitionNotificationTypes.ChallengePublished => Select(
                ("problem_title", 256),
                ("problem_category", 128)),
            CompetitionNotificationTypes.HintPublished => Select(
                ("problem_title", 256),
                ("hint_title", 256),
                ("hint_content", 1800)),
            CompetitionNotificationTypes.FirstBlood or
            CompetitionNotificationTypes.SecondBlood or
            CompetitionNotificationTypes.ThirdBlood => Select(
                ("problem_title", 256),
                ("team_name", 256),
                ("blood_rank", 16)),
            CompetitionNotificationTypes.TeamPenalized => Select(
                ("competition_name", 256),
                ("team_name", 256),
                ("penalty_type", 128)),
            CompetitionNotificationTypes.Announcement => Select(
                ("announcement_title", 256),
                ("announcement_content", 1800)),
            _ => null
        };
    }

    private static string ElementText(JsonElement element)
        => element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.ToString();

    private static string SerializeData(Dictionary<string, string> data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        while (json.Length > MaxDataJsonLength)
        {
            var field = data
                .Where(pair => pair.Value.Length > 0)
                .OrderByDescending(pair => pair.Value.Length)
                .First();
            var removeCount = Math.Min(field.Value.Length, json.Length - MaxDataJsonLength);
            data[field.Key] = Limit(field.Value, field.Value.Length - Math.Max(1, removeCount));
            json = JsonSerializer.Serialize(data, JsonOptions);
        }
        return json;
    }

    private static string Limit(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        var length = Math.Max(0, maxLength);
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        return value[..length];
    }

    private const int MaxDataJsonLength = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
