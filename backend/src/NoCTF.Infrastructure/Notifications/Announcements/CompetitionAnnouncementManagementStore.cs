using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications.Announcements;

public sealed class CompetitionAnnouncementManagementStore(NoCtfDbContext db) : ICompetitionAnnouncementManagementStore
{
    public async Task<ManagedCompetitionAnnouncementPage?> ListAsync(Guid competitionId, bool includeWithdrawn,
        int offset, int limit, bool desc, CancellationToken ct)
    {
        if (!await db.Competitions.AnyAsync(item => item.Id == competitionId, ct)) return null;
        var query = CompetitionAnnouncementProjection.Roots(db, competitionId);
        if (!includeWithdrawn) query = CompetitionAnnouncementProjection.ActiveMessages(db, query);
        var total = await query.CountAsync(ct);
        var ordered = desc ? query.OrderByDescending(item => item.SentAt).ThenByDescending(item => item.Id)
            : query.OrderBy(item => item.SentAt).ThenBy(item => item.Id);
        var roots = await ordered.Skip(offset).Take(limit).ToArrayAsync(ct);
        var changes = await CompetitionAnnouncementProjection.LatestAsync(db, roots, ct);
        var authors = roots.Where(item => item.SourceId != null).Select(item => item.SourceId!.Value).Distinct().ToArray();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(item => authors.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.UserName, ct);
        return new(roots.Select(root => Map(root, changes.GetValueOrDefault(root.Id), root.SourceId is Guid id ? names.GetValueOrDefault(id) : null)).ToArray(), total);
    }

    public async Task<ChangeCompetitionAnnouncementResult> ChangeAsync(ChangeCompetitionAnnouncementCommand command, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await ChangeOnceAsync(command, ct); }
            catch (Exception exception) when (RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                if (attempt == 2) return new(null, CompetitionAnnouncementFailure.ConcurrentChange);
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<ChangeCompetitionAnnouncementResult> ChangeOnceAsync(ChangeCompetitionAnnouncementCommand command, CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, IsolationLevel.Serializable, ct);
        var root = await CompetitionAnnouncementProjection.Roots(db, command.CompetitionId)
            .SingleOrDefaultAsync(item => item.Id == command.AnnouncementId, ct);
        if (root is null) return new(null, CompetitionAnnouncementFailure.NotFound);
        var latest = (await CompetitionAnnouncementProjection.LatestAsync(db, [root], ct)).GetValueOrDefault(root.Id);
        if (CompetitionAnnouncementProjection.Withdrawn(latest))
            return command.Action == CompetitionAnnouncementChangeAction.Withdraw ? new(Map(root, latest)) : new(null, CompetitionAnnouncementFailure.Withdrawn);
        var changedAt = latest is not null && latest.SentAt >= command.ChangedAt ? latest.SentAt.AddTicks(1) : command.ChangedAt;
        var change = new MessageNotification
        {
            Id = Guid.CreateVersion7(changedAt), SourceType = NotificationSourceType.User, SourceId = command.ActorId,
            TargetType = root.TargetType, TargetId = root.TargetId, CompetitionId = command.CompetitionId,
            RelatedType = EntityReferenceKind.Notification, RelatedId = root.Id, ThreadRootId = root.Id,
            ActionValue = (int)command.Action, SentAt = changedAt,
            Title = command.Action == CompetitionAnnouncementChangeAction.Edit ? command.Title : CompetitionAnnouncementProjection.Title(root, latest),
            Body = command.Action == CompetitionAnnouncementChangeAction.Edit ? command.Body : CompetitionAnnouncementProjection.Body(root, latest)
        };
        db.Notifications.Add(change);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(Map(root, change));
    }

    private static ManagedCompetitionAnnouncement Map(Notification root, Notification? change, string? authorName = null) => new(
        root.Id, CompetitionAnnouncementProjection.Title(root, change), CompetitionAnnouncementProjection.Body(root, change),
        root.TargetType == NotificationTargetType.CompetitionParticipants ? CompetitionAnnouncementAudience.Participants : CompetitionAnnouncementAudience.Collaborators,
        CompetitionAnnouncementProjection.Withdrawn(change) ? CompetitionAnnouncementState.Withdrawn : CompetitionAnnouncementState.Published,
        root.SourceId, authorName, root.SentAt, change?.SentAt ?? root.SentAt);
}
