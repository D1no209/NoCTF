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

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var createdAt = DateTimeOffset.UtcNow;
        foreach (var userId in recipients)
        {
            var notificationId = Guid.CreateVersion7(createdAt);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO notifications
                    (id, user_id, competition_id, entity_id, kind, source_event_key, payload_json, created_at)
                VALUES
                    ({notificationId}, {userId}, {competitionId}, {entityId}, {(short)kind}, {sourceEventKey}, {payloadJson}::jsonb, {createdAt})
                ON CONFLICT (user_id, source_event_key) DO NOTHING
                """,
                ct);
        }
    }
}
