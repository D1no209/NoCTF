using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Administration;

/// <summary>Proves notification ownership before deleting a competition. Reply context is not ownership.</summary>
internal sealed record CompetitionNotificationDeletionScope(Guid[] Ids, Guid[] ConflictingIds)
{
    public static async Task<CompetitionNotificationDeletionScope> LoadAsync(
        NoCtfDbContext db, Guid competitionId, bool lockRows, CancellationToken ct)
    {
        var teams = await db.Teams.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var challenges = await db.CompetitionChallenges.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var facts = await db.GameplayFacts.IgnoreQueryFilters().Where(x => x.CompetitionId == competitionId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var runtimes = await db.RuntimeInstances.Where(x => x.CompetitionId == competitionId)
            .Select(x => x.Id).ToArrayAsync(ct);
        var events = await db.CompetitionEvents.Where(x => x.CompetitionId == competitionId)
            .Select(x => x.Id).ToArrayAsync(ct);

        var directIds = await db.Notifications.AsNoTracking().Where(n =>
                n.Kind != NotificationKind.CompetitionForceDeleted
                && ((n.RelatedType == EntityReferenceKind.Competition && n.RelatedId == competitionId)
                    || (n.RelatedType == EntityReferenceKind.Team && teams.Contains(n.RelatedId!.Value))
                    || (n.RelatedType == EntityReferenceKind.CompetitionChallenge && challenges.Contains(n.RelatedId!.Value))
                    || (n.RelatedType == EntityReferenceKind.GameplayFact && facts.Contains(n.RelatedId!.Value))
                    || (n.RelatedType == EntityReferenceKind.RuntimeInstance && runtimes.Contains(n.RelatedId!.Value))
                    || (n.RelatedType == EntityReferenceKind.CompetitionEvent && events.Contains(n.RelatedId!.Value))
                    || (n.SourceType == NotificationSourceType.Competition && n.SourceId == competitionId)
                    || (n.SourceType == NotificationSourceType.Team && teams.Contains(n.SourceId!.Value))
                    || ((n.TargetType == NotificationTargetType.CompetitionCollaborators
                            || n.TargetType == NotificationTargetType.CompetitionParticipants) && n.TargetId == competitionId)
                    || (n.TargetType == NotificationTargetType.TeamMembers && teams.Contains(n.TargetId))))
            .Select(n => n.Id).ToArrayAsync(ct);

        // Lock roots before reading members: concurrent FK inserts must finish before this read,
        // or wait until deletion commits. Never lock or delete an entire unrelated thread via ReplyToId.
        if (lockRows)
            await LockAsync(db, directIds, ct);
        var roots = await db.Notifications.Where(n => directIds.Contains(n.Id) && n.ThreadRootId == null)
            .Select(n => n.Id).ToArrayAsync(ct);
        var candidates = await db.Notifications.AsNoTracking().Where(n =>
                n.Kind != NotificationKind.CompetitionForceDeleted
                && (directIds.Contains(n.Id) || roots.Contains(n.ThreadRootId!.Value)))
            .ToArrayAsync(ct);
        var ids = candidates.Select(n => n.Id).ToArray();
        if (lockRows)
            await LockAsync(db, ids, ct);

        bool ForeignRelated(Notification n) => n.RelatedType switch
        {
            EntityReferenceKind.Competition => n.RelatedId != competitionId,
            EntityReferenceKind.Team => !teams.Contains(n.RelatedId ?? Guid.Empty),
            EntityReferenceKind.CompetitionChallenge => !challenges.Contains(n.RelatedId ?? Guid.Empty),
            EntityReferenceKind.GameplayFact => !facts.Contains(n.RelatedId ?? Guid.Empty),
            EntityReferenceKind.RuntimeInstance => !runtimes.Contains(n.RelatedId ?? Guid.Empty),
            EntityReferenceKind.CompetitionEvent => !events.Contains(n.RelatedId ?? Guid.Empty),
            EntityReferenceKind.Notification => !ids.Contains(n.RelatedId ?? Guid.Empty),
            // Hint ids are embedded in rules, not independent rows: do not infer ownership.
            EntityReferenceKind.ChallengeHint => true,
            _ => false
        };

        var conflicts = candidates.Where(n =>
                ForeignRelated(n)
                || (n.SourceType == NotificationSourceType.Competition && n.SourceId != competitionId)
                || (n.SourceType == NotificationSourceType.Team && !teams.Contains(n.SourceId ?? Guid.Empty))
                || ((n.TargetType == NotificationTargetType.CompetitionCollaborators
                        || n.TargetType == NotificationTargetType.CompetitionParticipants) && n.TargetId != competitionId)
                || (n.TargetType == NotificationTargetType.TeamMembers && !teams.Contains(n.TargetId))
                || (n.ThreadRootId is Guid root && !roots.Contains(root))
                || (n.ReplyToId is Guid reply && !ids.Contains(reply)))
            .Select(n => n.Id).ToList();
        conflicts.AddRange(await db.Notifications.Where(n => !ids.Contains(n.Id)
                && (ids.Contains(n.ThreadRootId!.Value) || ids.Contains(n.ReplyToId!.Value)
                    || (n.RelatedType == EntityReferenceKind.Notification && ids.Contains(n.RelatedId!.Value))))
            .Select(n => n.Id).ToArrayAsync(ct));
        return new(ids, conflicts.Distinct().Order().ToArray());
    }

    private static Task LockAsync(NoCtfDbContext db, Guid[] ids, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM notifications WHERE id = ANY({ids}) ORDER BY id FOR UPDATE", ct);
}
