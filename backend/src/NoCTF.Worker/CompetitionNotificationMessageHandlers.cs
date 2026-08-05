using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Worker;

public static class CompetitionNotificationMessageHandlers
{
    public static Task Handle(
        BloodAwarded message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverAsync(
            message.CompetitionId,
            message.CompetitionChallengeId,
            NotificationKind.BloodAwarded,
            $"blood:{message.CompetitionChallengeId:N}:{(int)message.BloodRank}",
            new BloodAwardedPayload(
                message.CompetitionId,
                message.CompetitionChallengeId,
                message.ChallengeTitle,
                message.BloodRank,
                message.TeamId,
                message.TeamName,
                message.OccurredAt),
            requiredTeamId: null,
            ct);

    public static Task Handle(
        ChallengePublished message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverAsync(
            message.CompetitionId,
            message.CompetitionChallengeId,
            NotificationKind.ChallengePublished,
            $"challenge-published:{message.CompetitionChallengeId:N}:{message.Revision}",
            new ChallengePublishedPayload(
                message.CompetitionId,
                message.CompetitionChallengeId,
                message.ChallengeTitle,
                message.Direction,
                message.PublishedAt),
            requiredTeamId: null,
            ct);

    public static async Task Handle(
        PublishHintNotification message,
        NoCtfDbContext db,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct)
    {
        var isCurrent = await db.Set<NoCTF.Domain.Challenges.CompetitionChallengeHint>()
            .AsNoTracking()
            .AnyAsync(hint =>
                hint.Id == message.HintId
                && hint.CompetitionChallengeId == message.CompetitionChallengeId
                && hint.DeletedAt == null
                && hint.PublicationRevision == message.PublicationRevision
                && hint.PublishedAt == message.PublishedAt
                && hint.PublishedAt <= DateTimeOffset.UtcNow
                && db.CompetitionChallenges.Any(challenge =>
                    challenge.Id == hint.CompetitionChallengeId
                    && challenge.CompetitionId == message.CompetitionId),
                ct);
        if (!isCurrent)
            return;

        await delivery.DeliverAsync(
            message.CompetitionId,
            message.HintId,
            NotificationKind.HintPublished,
            $"hint-published:{message.HintId:N}:{message.PublicationRevision}",
            new HintPublishedPayload(
                message.CompetitionId,
                message.CompetitionChallengeId,
                message.HintId,
                message.ChallengeTitle,
                message.Cost,
                message.PublishedAt),
            requiredTeamId: null,
            ct);
    }

    public static Task Handle(
        TeamBanned message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverAsync(
            message.CompetitionId,
            message.TeamId,
            NotificationKind.TeamBanned,
            $"team-banned:{message.TeamId:N}:{message.BannedAt.UtcTicks}",
            new TeamBannedPayload(
                message.CompetitionId,
                message.TeamId,
                message.TeamName,
                message.BannedAt),
            message.TeamId,
            ct);

    public static Task Handle(
        DeliverCompetitionQuestionNotification message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverToUsersAsync(
            message.CompetitionId,
            message.QuestionId,
            message.Kind,
            $"competition-question:{message.QuestionId:N}:{message.EntryId?.ToString("N") ?? "root"}:{message.Revision}:{(short)message.Event}",
            new CompetitionQuestionActivityPayload(
                message.CompetitionId,
                message.QuestionId,
                message.EntryId,
                message.Event,
                message.Title,
                message.OccurredAt),
            message.RecipientUserIds,
            ct);
}
