using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awd;

internal static class AwdAdjudicationNotification
{
    internal static async Task AddAsync(NoCtfDbContext db, GameplayFact fact, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Notifications.AnyAsync(notification => notification.Kind == NotificationKind.ManagementFailure
                && notification.RelatedType == EntityReferenceKind.GameplayFact && notification.RelatedId == fact.Id, ct))
            return;
        db.Notifications.Add(new ManagementFailureNotification
        {
            Id = Guid.CreateVersion7(now), SourceType = NotificationSourceType.System,
            TargetType = NotificationTargetType.CompetitionCollaborators, TargetId = fact.CompetitionId,
            Code = "awd_checker_adjudication_required", CompetitionChallengeId = fact.CompetitionChallengeId,
            RuntimeInstanceId = fact.Awd!.RuntimeId, GameplayFactId = fact.Id,
            RelatedType = EntityReferenceKind.GameplayFact, RelatedId = fact.Id, SentAt = now
        });
    }
}
