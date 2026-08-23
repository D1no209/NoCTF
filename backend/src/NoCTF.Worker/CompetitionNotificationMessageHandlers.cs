using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Shared;
using NoCTF.Domain.Competitions;

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
            $"challenge-published:{message.CompetitionChallengeId:N}:{message.PublishedAt.UtcTicks}",
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
        CancellationToken ct,
        ICompetitionEventRecorder? eventRecorder = null)
    {
        var challenge = await db.CompetitionChallenges.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == message.CompetitionChallengeId
                && candidate.CompetitionId == message.CompetitionId, ct);
        var isCurrent = challenge is not null
            && challenge.Hints.Any(hint =>
                hint.Id == message.HintId
                && hint.HiddenAt == null
                && hint.PublishedAt == message.PublishedAt
                && hint.PublishedAt <= DateTimeOffset.UtcNow);
        if (!isCurrent)
            return;

        var alreadyRecorded = await db.CompetitionEvents.AsNoTracking().AnyAsync(
            item => item.CompetitionId == message.CompetitionId
                && item.SubjectType == EntityReferenceKind.ChallengeHint
                && item.SubjectId == message.HintId
                && item.Kind == CompetitionEventKind.HintPublished
                && item.OccurredAt == message.PublishedAt,
            ct);
        if (!alreadyRecorded)
        {
            var events = eventRecorder ?? NullCompetitionEventRecorder.Instance;
            await events.RecordAsync(new(
                message.CompetitionId,
                CompetitionEventKind.HintPublished,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                message.PublishedAt,
                CompetitionChallengeId: message.CompetitionChallengeId,
                HintId: message.HintId), ct);
        }

        await delivery.DeliverAsync(
            message.CompetitionId,
            message.HintId,
            NotificationKind.HintPublished,
            $"hint-published:{message.HintId:N}:{message.PublishedAt.UtcTicks}",
            new HintPublishedPayload(
                message.CompetitionId,
                message.CompetitionChallengeId,
                message.HintId,
                message.ChallengeTitle,
                message.Cost,
                message.PublishedAt),
            requiredTeamId: null,
            ct);
        await db.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        TeamBanned message,
        NoCtfDbContext db,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct,
        ICompetitionEventRecorder? eventRecorder = null)
    {
        await delivery.DeliverAsync(
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

        var teamTrack = await db.Teams.AsNoTracking()
            .Where(team => team.Id == message.TeamId && team.CompetitionId == message.CompetitionId)
            .Select(team => team.TrackKey)
            .SingleOrDefaultAsync(ct);
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == message.CompetitionId)
            .Select(item => new { item.Mode, item.TrackConfigurationJson })
            .SingleOrDefaultAsync(ct);
        if (competition is null
            || CompetitionTrackConfiguration.ParseOrDefault(
                competition.Mode,
                competition.TrackConfigurationJson).Find(teamTrack)?.IsInternal == true)
            return;

        if (message.AnnouncementKind is not { } announcementKind)
            return;

        if (announcementKind == TeamBanAnnouncementKind.ConfirmedCheating)
        {
            var alreadyRecorded = await db.CompetitionEvents.AsNoTracking().AnyAsync(
                item => item.CompetitionId == message.CompetitionId
                    && item.SubjectType == EntityReferenceKind.Team
                    && item.SubjectId == message.TeamId
                    && item.Kind == CompetitionEventKind.TeamBanned
                    && item.Visibility == CompetitionEventVisibility.Public
                    && item.OccurredAt == message.BannedAt,
                ct);
            if (!alreadyRecorded)
            {
                var events = eventRecorder ?? NullCompetitionEventRecorder.Instance;
                await events.RecordAsync(new(
                    message.CompetitionId,
                    CompetitionEventKind.TeamBanned,
                    CompetitionEventLevel.Warning,
                    CompetitionEventVisibility.Public,
                    message.BannedAt,
                    TeamId: message.TeamId), ct);
                await db.SaveChangesAsync(ct);
            }
        }

        var body = announcementKind switch
        {
            TeamBanAnnouncementKind.ConfirmedCheating =>
                $"队伍「{message.TeamName}」经核实存在作弊行为，现已由赛事组委会予以封禁。",
            TeamBanAnnouncementKind.RuleViolation =>
                $"队伍「{message.TeamName}」因违反赛事规则，现已由赛事组委会予以封禁。",
            _ => throw new ArgumentOutOfRangeException(
                nameof(message),
                announcementKind,
                "Unsupported team ban announcement kind.")
        };
        await delivery.DeliverAsync(
            message.CompetitionId,
            message.TeamId,
            NotificationKind.CompetitionAnnouncement,
            $"team-ban-announcement:{message.TeamId:N}:{message.BannedAt.UtcTicks}:{announcementKind}",
            new TeamBanAnnouncementPayload(
                message.CompetitionId,
                message.TeamId,
                message.TeamName,
                announcementKind,
                "赛事纪律公告",
                body,
                message.BannedAt),
            requiredTeamId: null,
            ct);
    }

    public static async Task Handle(
        ForeignTeamFlagDetected message,
        NoCtfDbContext db,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(item => item.Id == message.CompetitionId && item.DeletedAt == null)
            .Select(item => new
            {
                item.OwnerId,
                item.ManagerIds,
                item.JudgeIds
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return;

        var administratorIds = await db.Users.AsNoTracking()
            .Where(user => user.Role == NoCTF.Domain.Identity.UserRole.Administrator)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        var recipients = administratorIds
            .Concat(competition.ManagerIds)
            .Concat(competition.JudgeIds)
            .Append(competition.OwnerId)
            .Distinct()
            .ToArray();
        await delivery.DeliverToUsersAsync(
            message.CompetitionId,
            message.GameplayFactId,
            NotificationKind.CheatIncidentDetected,
            $"cheat-incident:{message.GameplayFactId:N}",
            new CheatIncidentDetectedPayload(
                message.CompetitionId,
                message.GameplayFactId,
                message.SourceTeamId,
                message.OwnerTeamId,
                message.ActorUserId,
                message.CompetitionChallengeId,
                message.DetectedAt),
            recipients,
            ct);
    }

    public static Task Handle(
        TeamBanCorrected message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverAsync(
            message.CompetitionId,
            message.BanEventId,
            NotificationKind.TeamBanCorrected,
            $"team-ban-corrected:{message.BanEventId:N}:{message.CorrectedAt.UtcTicks}",
            new TeamBanCorrectedPayload(
                message.CompetitionId,
                message.TeamId,
                message.TeamName,
                message.CorrectedAt),
            requiredTeamId: message.TeamId,
            ct);

    public static Task Handle(
        DeliverCompetitionQuestionNotification message,
        CompetitionNotificationDelivery delivery,
        CancellationToken ct) =>
        delivery.DeliverToUsersAsync(
            message.CompetitionId,
            message.QuestionId,
            message.Kind,
            $"competition-question:{message.QuestionId:N}:{message.EntryId?.ToString("N") ?? "root"}:{message.OccurredAt.UtcTicks}:{(short)message.Event}",
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
