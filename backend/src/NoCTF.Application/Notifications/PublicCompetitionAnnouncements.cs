namespace NoCTF.Application.Notifications;

public sealed record PublicCompetitionAnnouncementView(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset PublishedAt);

public enum PublicCompetitionAnnouncementReadState : short
{
    Available,
    CompetitionNotFound
}

public sealed record PublicCompetitionAnnouncementPage(
    PublicCompetitionAnnouncementReadState State,
    IReadOnlyList<PublicCompetitionAnnouncementView>? Items = null);

public interface IPublicCompetitionAnnouncementReader
{
    Task<PublicCompetitionAnnouncementPage> ListAsync(
        Guid competitionId,
        DateTimeOffset? beforePublishedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed class ListPublicCompetitionAnnouncements(
    IPublicCompetitionAnnouncementReader reader)
{
    public Task<PublicCompetitionAnnouncementPage> ExecuteAsync(
        Guid competitionId,
        DateTimeOffset? beforePublishedAt,
        Guid? beforeId,
        int limit,
        CancellationToken cancellationToken = default) =>
        reader.ListAsync(
            competitionId,
            beforePublishedAt,
            beforeId,
            limit,
            cancellationToken);
}
