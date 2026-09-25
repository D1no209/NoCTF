using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Notifications;
using NoCTF.Application.Notifications;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class CompetitionNotificationDelivery(
    NoCtfDbContext db,
    ICompetitionEventRecorder? eventRecorder = null,
    TimeProvider? clock = null)
    : ICompetitionAnnouncementPublisher
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task DeliverAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        Guid? requiredTeamId,
        CancellationToken ct)
    {
        _ = entityId;
        var targetType = requiredTeamId is null
            ? NotificationTargetType.CompetitionParticipants
            : NotificationTargetType.TeamMembers;
        var targetId = requiredTeamId ?? competitionId;
        var alreadyDelivered = await db.Notifications.AsNoTracking().AnyAsync(
            item => item.TargetType == targetType
                && item.TargetId == targetId
                && item.Kind == kind
                && item.SourceEventKey == sourceEventKey
                && item.RelatedType == EntityReferenceKind.Competition
                && item.RelatedId == competitionId,
            ct);
        if (alreadyDelivered)
            return;
        var notification = NotificationGeneratedCatalog.Create(kind);
        notification.Id = Guid.CreateVersion7();
        notification.SourceType = NotificationSourceType.System;
        notification.TargetType = targetType;
        notification.TargetId = targetId;
        notification.SourceEventKey = sourceEventKey;
        ApplyPayload(notification, payload);
        notification.RelatedType = NoCTF.Domain.Shared.EntityReferenceKind.Competition;
        notification.RelatedId = competitionId;
        notification.SentAt = timeProvider.GetUtcNow();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeliverToUsersAsync<TPayload>(
        Guid competitionId,
        Guid entityId,
        NotificationKind kind,
        string sourceEventKey,
        TPayload payload,
        IReadOnlyList<Guid> recipientUserIds,
        CancellationToken ct)
    {
        if (recipientUserIds.Count == 0)
            return;
        var recipients = await db.Users.AsNoTracking()
            .Where(user => recipientUserIds.Contains(user.Id)
                && user.AccountStatus == NoCTF.Domain.Identity.UserAccountStatus.Active)
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        if (recipients.Length == 0)
            return;

        _ = entityId;
        var sentAt = timeProvider.GetUtcNow();
        var alreadyDelivered = await db.Notifications.AsNoTracking()
            .Where(item => item.TargetType == NotificationTargetType.User
                && recipients.Contains(item.TargetId)
                && item.Kind == kind
                && item.SourceEventKey == sourceEventKey
                && item.RelatedType == EntityReferenceKind.Competition
                && item.RelatedId == competitionId)
            .Select(item => item.TargetId)
            .ToArrayAsync(ct);
        var pendingRecipients = recipients.Except(alreadyDelivered).ToArray();
        db.Notifications.AddRange(pendingRecipients.Select((userId, index) =>
        {
            var pending = NotificationGeneratedCatalog.Create(kind);
            pending.Id = Guid.CreateVersion7(sentAt.AddTicks(index));
            pending.SourceType = NotificationSourceType.System;
            pending.TargetType = NotificationTargetType.User;
            pending.TargetId = userId;
            pending.SourceEventKey = sourceEventKey;
            ApplyPayload(pending, payload);
            pending.RelatedType = NoCTF.Domain.Shared.EntityReferenceKind.Competition;
            pending.RelatedId = competitionId;
            pending.SentAt = sentAt;
            return pending;
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationView?> PublishAsync(
        PublishCompetitionAnnouncementCommand command,
        CancellationToken ct)
    {
        var exists = await db.Competitions.AsNoTracking()
            .AnyAsync(competition => competition.Id == command.CompetitionId, ct);
        if (!exists)
            return null;
        var notification = new CompetitionAnnouncementNotification
        {
            Id = Guid.CreateVersion7(command.PublishedAt),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorUserId,
            TargetType = command.Audience == CompetitionAnnouncementAudience.Participants
                ? NotificationTargetType.CompetitionParticipants
                : NotificationTargetType.CompetitionCollaborators,
            TargetId = command.CompetitionId,
            Subject = command.Title,
            Title = command.Title,
            Body = command.Body,
            CompetitionId = command.CompetitionId,
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = command.CompetitionId,
            SentAt = command.PublishedAt
        };
        db.Notifications.Add(notification);
        await events.RecordAsync(new(
            command.CompetitionId,
            CompetitionEventKind.AnnouncementPublished,
            CompetitionEventLevel.Information,
            command.Audience == CompetitionAnnouncementAudience.Participants
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            command.PublishedAt,
            ActorUserId: command.ActorUserId,
            QuestionId: notification.Id), ct);
        await db.SaveChangesAsync(ct);
        return new(
            notification.Id,
            notification.SourceType,
            notification.SourceId,
            notification.TargetType,
            notification.TargetId,
            notification.Kind,
            NotificationContentProjection.Create(notification),
            notification.RelatedType,
            notification.RelatedId,
            notification.ThreadRootId,
            notification.ReplyToId,
            notification.SentAt);
    }

    private static void ApplyPayload<TPayload>(Notification notification, TPayload payload)
    {
        switch (payload)
        {
            case BloodAwardedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.CompetitionChallengeId = value.CompetitionChallengeId;
                notification.ChallengeTitle = value.ChallengeTitle;
                notification.ActionValue = (int)value.BloodRank;
                notification.TeamId = value.TeamId;
                notification.TeamName = value.TeamName;
                notification.PayloadOccurredAt = value.OccurredAt;
                return;
            case ChallengePublishedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.CompetitionChallengeId = value.CompetitionChallengeId;
                notification.ChallengeTitle = value.ChallengeTitle;
                notification.Direction = value.Direction;
                notification.PayloadOccurredAt = value.PublishedAt;
                return;
            case HintPublishedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.CompetitionChallengeId = value.CompetitionChallengeId;
                notification.HintId = value.HintId;
                notification.ChallengeTitle = value.ChallengeTitle;
                notification.Value = value.Cost;
                notification.PayloadOccurredAt = value.PublishedAt;
                return;
            case TeamBannedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.TeamId = value.TeamId;
                notification.TeamName = value.TeamName;
                notification.PayloadOccurredAt = value.BannedAt;
                return;
            case TeamBanAnnouncementPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.TeamId = value.TeamId;
                notification.TeamName = value.TeamName;
                notification.ActionValue = (int)value.Kind;
                notification.Title = value.Title;
                notification.Subject = value.Title;
                notification.Body = value.Body;
                notification.PayloadOccurredAt = value.BannedAt;
                return;
            case TeamBanCorrectedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.TeamId = value.TeamId;
                notification.TeamName = value.TeamName;
                notification.PayloadOccurredAt = value.CorrectedAt;
                return;
            case TeamBanAppealSubmittedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.AppealEventId = value.AppealEventId;
                notification.TeamId = value.TeamId;
                notification.TeamName = value.TeamName;
                notification.PayloadOccurredAt = value.SubmittedAt;
                return;
            case CompetitionQuestionActivityPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.EntryId = value.EntryId;
                notification.ActionValue = (int)value.Event;
                notification.Title = value.Title;
                notification.PayloadOccurredAt = value.OccurredAt;
                return;
            case CheatIncidentDetectedPayload value:
                notification.CompetitionId = value.CompetitionId;
                notification.GameplayFactId = value.GameplayFactId;
                notification.SourceTeamId = value.SourceTeamId;
                notification.OwnerTeamId = value.OwnerTeamId;
                notification.ActorUserId = value.ActorUserId;
                notification.CompetitionChallengeId = value.CompetitionChallengeId;
                notification.PayloadOccurredAt = value.DetectedAt;
                return;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(payload),
                    payload?.GetType(),
                    "Unsupported notification payload type.");
        }
    }
}
