using NoCTF.Domain.Notifications;

namespace NoCTF.Application.Notifications;

public enum CompetitionAnnouncementState { Published, Withdrawn }
public enum CompetitionAnnouncementFailure { NotFound, Withdrawn, InvalidContent, ConcurrentChange }
public sealed record ManagedCompetitionAnnouncement(Guid Id, string Title, string Body,
    CompetitionAnnouncementAudience Audience, CompetitionAnnouncementState State, Guid? AuthorId,
    string? AuthorName, DateTimeOffset PublishedAt, DateTimeOffset UpdatedAt);
public sealed record ManagedCompetitionAnnouncementPage(IReadOnlyList<ManagedCompetitionAnnouncement> Items, int Total);
public sealed record ChangeCompetitionAnnouncementCommand(Guid CompetitionId, Guid AnnouncementId, Guid ActorId,
    CompetitionAnnouncementChangeAction Action, string? Title, string? Body, DateTimeOffset ChangedAt);
public sealed record ChangeCompetitionAnnouncementResult(ManagedCompetitionAnnouncement? Announcement,
    CompetitionAnnouncementFailure? Failure = null);

public interface ICompetitionAnnouncementManagementStore
{
    Task<ManagedCompetitionAnnouncementPage?> ListAsync(Guid competitionId, bool includeWithdrawn, int offset, int limit, bool desc, CancellationToken ct);
    Task<ChangeCompetitionAnnouncementResult> ChangeAsync(ChangeCompetitionAnnouncementCommand command, CancellationToken ct);
}

public sealed class ManageCompetitionAnnouncements(ICompetitionAnnouncementManagementStore store)
{
    public Task<ManagedCompetitionAnnouncementPage?> ListAsync(Guid competitionId, bool includeWithdrawn, int offset, int limit, bool desc, CancellationToken ct) =>
        store.ListAsync(competitionId, includeWithdrawn, offset, limit, desc, ct);

    public Task<ChangeCompetitionAnnouncementResult> ChangeAsync(ChangeCompetitionAnnouncementCommand command, CancellationToken ct)
    {
        if (command.Action == CompetitionAnnouncementChangeAction.Edit
            && (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > 160
                || string.IsNullOrWhiteSpace(command.Body) || command.Body.Trim().Length > 16_000))
            return Task.FromResult(new ChangeCompetitionAnnouncementResult(null, CompetitionAnnouncementFailure.InvalidContent));
        return store.ChangeAsync(command with { Title = command.Title?.Trim(), Body = command.Body?.Trim() }, ct);
    }
}
