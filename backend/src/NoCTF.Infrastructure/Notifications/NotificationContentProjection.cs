using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Notifications;

namespace NoCTF.Infrastructure.Notifications;

internal static class NotificationContentProjection
{
    internal static NotificationContent Create(Notification item) => item.Kind switch
    {
        NotificationKind.Message => new MessageNotificationContent(
            item.Body,
            item.PreviousQuestionStatus,
            item.QuestionStatus,
            item.QuestionActorRole),
        NotificationKind.CompetitionAnnouncement => new CompetitionAnnouncementNotificationContent(
            item.Title,
            item.Body,
            item.TeamId,
            item.TeamName,
            item.ActionValue is { } announcementKind
                ? (TeamBanAnnouncementKind)announcementKind
                : null),
        NotificationKind.QuestionOpened => new QuestionOpenedNotificationContent(
            item.QuestionSubject,
            item.Title,
            item.Body,
            item.TeamId,
            item.CompetitionChallengeId,
            item.GameplayFactId,
            item.QuestionStatus,
            item.QuestionActorRole),
        NotificationKind.QuestionStatusChanged => new QuestionStatusChangedNotificationContent(
            item.PreviousQuestionStatus,
            item.QuestionStatus,
            item.QuestionActorRole),
        NotificationKind.CompetitionLifecycleChanged =>
            new CompetitionLifecycleChangedNotificationContent(item.StateValue, item.Reason),
        NotificationKind.TeamRegistrationChanged =>
            new TeamRegistrationChangedNotificationContent(item.TeamId, item.StateValue, item.Reason),
        NotificationKind.GameplayFactAdjudicated => new GameplayFactAdjudicatedNotificationContent(
            item.GameplayFactId,
            item.GameplayFactState,
            item.GameplayFactResult,
            item.GameplayFactFailureCode),
        NotificationKind.RuntimeStateChanged => new RuntimeStateChangedNotificationContent(
            item.Code,
            item.CompetitionChallengeId,
            item.RuntimeInstanceId,
            item.ChallengeFlagId,
            item.RuntimeState),
        NotificationKind.StartGateFailed => new StartGateFailedNotificationContent(item.Code, item.Reason),
        NotificationKind.ManagementFailure => new ManagementFailureNotificationContent(
            item.Code,
            item.CompetitionChallengeId,
            item.RuntimeInstanceId,
            item.GameplayFactId),
        NotificationKind.BloodAwarded => new BloodAwardedNotificationContent(
            item.CompetitionId,
            item.CompetitionChallengeId,
            item.ChallengeTitle,
            item.ActionValue is { } rank ? (LeaderboardBloodRank)rank : null,
            item.TeamId,
            item.TeamName,
            item.PayloadOccurredAt),
        NotificationKind.ChallengePublished => new ChallengePublishedNotificationContent(
            item.CompetitionId,
            item.CompetitionChallengeId,
            item.ChallengeTitle,
            item.Direction,
            item.PayloadOccurredAt),
        NotificationKind.HintPublished => new HintPublishedNotificationContent(
            item.CompetitionId,
            item.CompetitionChallengeId,
            item.HintId,
            item.ChallengeTitle,
            item.Value,
            item.PayloadOccurredAt),
        NotificationKind.TeamBanned => new TeamBannedNotificationContent(
            item.CompetitionId,
            item.TeamId,
            item.TeamName,
            item.PayloadOccurredAt),
        NotificationKind.CheatIncidentDetected => new CheatIncidentDetectedNotificationContent(
            item.CompetitionId,
            item.GameplayFactId,
            item.SourceTeamId,
            item.OwnerTeamId,
            item.ActorUserId,
            item.CompetitionChallengeId,
            item.PayloadOccurredAt),
        NotificationKind.TeamBanCorrected => new TeamBanCorrectedNotificationContent(
            item.CompetitionId,
            item.TeamId,
            item.TeamName,
            item.PayloadOccurredAt),
        NotificationKind.TeamBanAppealSubmitted => new TeamBanAppealSubmittedNotificationContent(
            item.CompetitionId,
            item.AppealEventId,
            item.TeamId,
            item.TeamName,
            item.PayloadOccurredAt),
        NotificationKind.PlatformAuditExported => new PlatformAuditExportedNotificationContent(
            item.ActionValue,
            item.CompetitionId,
            item.ActorUserId,
            item.RangeFrom,
            item.RangeTo,
            item.Value),
        NotificationKind.UserAccountLifecycleChanged => new UserAccountLifecycleChangedNotificationContent(
            item.UserId,
            item.UserName,
            item.UserLifecycleAction,
            item.Reason,
            item.Automatic),
        NotificationKind.CompetitionForceDeleted => new CompetitionForceDeletedNotificationContent(
            item.CompetitionId,
            item.Title,
            item.Reason,
            item.ReferenceCounts.OrderBy(reference => reference.ReferenceKind)
                .Select(reference => new CompetitionForceDeletedReference(
                    reference.ReferenceKind,
                    reference.Count))
                .ToArray()),
        _ => throw new InvalidOperationException(
            $"Notification kind {item.Kind} is not part of the public notification contract.")
    };
}
