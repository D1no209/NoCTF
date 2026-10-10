using NoCTF.Application.Notifications;

namespace NoCTF.Application.LiveSolo.Media;

public sealed record LiveSoloMediaAlertCreated(Guid NotificationId);
public interface ILiveSoloMediaAlertReader
{
    Task<NotificationAudience?> AudienceAsync(Guid notificationId, CancellationToken ct);
}
public sealed class PublishLiveSoloMediaAlert(ILiveSoloMediaAlertReader reader, INotificationChangePublisher publisher)
{
    public async Task ExecuteAsync(LiveSoloMediaAlertCreated message, CancellationToken ct)
    {
        if (await reader.AudienceAsync(message.NotificationId, ct) is { } audience)
            await publisher.PublishAsync(new([audience]), ct);
    }
}
