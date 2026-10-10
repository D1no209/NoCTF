using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Media;
using NoCTF.Application.LiveSolo.Realtime;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.LiveSolo.Media;

public sealed partial class LiveSoloMediaStore
{
    private async Task AlertAsync(LiveSoloMatch match, LiveSoloMediaSession session, LiveSoloMediaAlertKind reason,
        LiveSoloScreenState? state, LiveSoloMediaParticipant? member, CancellationToken ct)
    {
        var key = $"live-solo-media:{session.Id:N}:{session.ConcurrencyStamp:N}:{member?.Identity ?? session.RoomIdentity}:{(short)reason}";
        if (await db.Notifications.AnyAsync(x => x.Kind == NotificationKind.LiveSoloMediaInterrupted && x.SourceEventKey == key, ct)) return;
        var alert = NotificationGeneratedCatalog.Create(NotificationKind.LiveSoloMediaInterrupted);
        alert.Id = Guid.CreateVersion7(clock.GetUtcNow()); alert.SourceType = NotificationSourceType.System; alert.SourceId = session.Id;
        alert.TargetType = NotificationTargetType.CompetitionCollaborators; alert.TargetId = match.CompetitionId;
        alert.RelatedType = EntityReferenceKind.Competition; alert.RelatedId = match.CompetitionId; alert.CompetitionId = match.CompetitionId;
        alert.LiveSoloMatchId = match.Id; alert.LiveSoloMediaSessionId = session.Id; alert.LiveSoloMediaAlertKind = reason; alert.LiveSoloScreenState = state;
        alert.UserId = member?.UserId; alert.TeamId = member?.TeamId;
        if (member is not null)
        {
            alert.UserName = await db.Users.IgnoreQueryFilters().Where(x => x.Id == member.UserId).Select(x => x.UserName).SingleAsync(ct);
            alert.TeamName = await db.Teams.IgnoreQueryFilters().Where(x => x.Id == member.TeamId).Select(x => x.Name).SingleAsync(ct);
        }
        alert.SentAt = clock.GetUtcNow(); alert.PayloadOccurredAt = alert.SentAt; alert.SourceEventKey = key;
        db.Notifications.Add(alert); await messages.PublishAsync(new LiveSoloMediaAlertCreated(alert.Id));
    }
    private ValueTask MediaChangedAsync(LiveSoloMatch match) => messages.PublishAsync(new LiveSoloMatchChanged(match.CompetitionId, match.Id, clock.GetUtcNow()));
}
