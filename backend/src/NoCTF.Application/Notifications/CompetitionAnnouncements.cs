namespace NoCTF.Application.Notifications;

public enum CompetitionAnnouncementAudience : short
{
    Collaborators,
    Participants
}

public sealed record PublishCompetitionAnnouncementCommand(
    Guid CompetitionId,
    Guid ActorUserId,
    string Title,
    string Body,
    CompetitionAnnouncementAudience Audience,
    DateTimeOffset PublishedAt);

public interface ICompetitionAnnouncementPublisher
{
    Task<NotificationView?> PublishAsync(
        PublishCompetitionAnnouncementCommand command,
        CancellationToken cancellationToken);
}

public sealed class PublishCompetitionAnnouncement(
    ICompetitionAnnouncementPublisher publisher)
{
    public Task<NotificationView?> ExecuteAsync(
        PublishCompetitionAnnouncementCommand command,
        CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(command with
        {
            Title = command.Title.Trim(),
            Body = command.Body.Trim()
        }, cancellationToken);
}
