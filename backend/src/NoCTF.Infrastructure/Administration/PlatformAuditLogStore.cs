using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.PlatformLogs;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Notifications;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAuditLogStore(NoCtfDbContext db) : IPlatformAuditLogStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PlatformAuditView>> QueryAsync(
        PlatformAuditQuery query,
        CancellationToken ct)
    {
        var items = new List<PlatformAuditView>(query.Limit * 2);
        if (query.Kind is not PlatformAuditKind.UserAccountLifecycle)
        {
            var competitionEvents = db.CompetitionEvents.AsNoTracking();
            if (query.From is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt >= query.From.Value);
            if (query.To is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt <= query.To.Value);
            if (query.CompetitionId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.CompetitionId == query.CompetitionId.Value);
            if (query.ActorId is not null)
                competitionEvents = competitionEvents.Where(item =>
                    item.ActorUserId == query.ActorId.Value);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                competitionEvents = competitionEvents.Where(item =>
                    item.OccurredAt < beforeOccurredAt
                    || item.OccurredAt == beforeOccurredAt
                    && item.Id.CompareTo(beforeId) < 0);
            }
            competitionEvents = query.Kind switch
            {
                PlatformAuditKind.CompetitionLifecycle => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.CompetitionLifecycleChanged),
                PlatformAuditKind.CompetitionLeaderboardVisibility => competitionEvents.Where(item =>
                    item.Kind == CompetitionEventKind.LeaderboardVisibilityChanged),
                PlatformAuditKind.CompetitionEvent => competitionEvents.Where(item =>
                    item.Kind != CompetitionEventKind.CompetitionLifecycleChanged
                    && item.Kind != CompetitionEventKind.LeaderboardVisibilityChanged),
                _ => competitionEvents
            };

            var eventItems = await competitionEvents
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct);
            var competitionIds = eventItems
                .Select(item => item.CompetitionId)
                .Distinct()
                .ToArray();
            var competitionTitles = await db.Competitions.AsNoTracking()
                .Where(item => competitionIds.Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, item => item.Title, ct);
            items.AddRange(eventItems.Select(item => new PlatformAuditView(
                item.Id,
                MapKind(item.Kind),
                item.CompetitionId,
                item.CompetitionId,
                item.ActorUserId,
                null,
                null,
                null,
                null,
                null,
                item.Kind,
                item.Level,
                item.Visibility,
                Reference(item, EntityReferenceKind.User),
                Reference(item, EntityReferenceKind.Team),
                Reference(item, EntityReferenceKind.CompetitionChallenge),
                Reference(item, EntityReferenceKind.RuntimeInstance),
                Reference(item, EntityReferenceKind.GameplayFact),
                Reference(item, EntityReferenceKind.Notification),
                null,
                null,
                null,
                competitionTitles.GetValueOrDefault(item.CompetitionId),
                item.Reason,
                false,
                item.OccurredAt)));
        }

        if (query.CompetitionId is null
            && query.Kind is null or PlatformAuditKind.UserAccountLifecycle)
        {
            var lifecycleFacts = db.Notifications.AsNoTracking().Where(notification =>
                notification.Kind == NotificationKind.UserAccountLifecycleChanged
                && notification.TargetType == NotificationTargetType.PlatformAdministrators);
            if (query.From is not null)
                lifecycleFacts = lifecycleFacts.Where(notification =>
                    notification.SentAt >= query.From.Value);
            if (query.To is not null)
                lifecycleFacts = lifecycleFacts.Where(notification =>
                    notification.SentAt <= query.To.Value);
            if (query.ActorId is not null)
                lifecycleFacts = lifecycleFacts.Where(notification =>
                    notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == query.ActorId.Value);
            if (query.BeforeOccurredAt is DateTimeOffset beforeOccurredAt
                && query.BeforeId is Guid beforeId)
            {
                lifecycleFacts = lifecycleFacts.Where(notification =>
                    notification.SentAt < beforeOccurredAt
                    || notification.SentAt == beforeOccurredAt
                    && notification.Id.CompareTo(beforeId) < 0);
            }

            var lifecycleItems = await lifecycleFacts
                .OrderByDescending(notification => notification.SentAt)
                .ThenByDescending(notification => notification.Id)
                .Take(query.Limit)
                .ToArrayAsync(ct);
            items.AddRange(lifecycleItems.Select(ToAuditView));
        }

        return items
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Id)
            .Take(query.Limit)
            .ToArray();
    }

    private static PlatformAuditKind MapKind(CompetitionEventKind kind) => kind switch
    {
        CompetitionEventKind.CompetitionLifecycleChanged =>
            PlatformAuditKind.CompetitionLifecycle,
        CompetitionEventKind.LeaderboardVisibilityChanged =>
            PlatformAuditKind.CompetitionLeaderboardVisibility,
        _ => PlatformAuditKind.CompetitionEvent
    };

    private static Guid? Reference(CompetitionEvent item, EntityReferenceKind kind) =>
        item.SubjectType == kind ? item.SubjectId : item.RelatedType == kind ? item.RelatedId : null;

    private static PlatformAuditView ToAuditView(Notification notification)
    {
        var fact = JsonSerializer.Deserialize<UserAccountLifecycleFact>(
            notification.ContentJson,
            JsonOptions) ?? throw new InvalidOperationException(
            $"Notification {notification.Id} has no user lifecycle payload.");
        return new(
            Id: notification.Id,
            Kind: PlatformAuditKind.UserAccountLifecycle,
            SubjectId: fact.TargetUserId,
            CompetitionId: null,
            ActorId: notification.SourceId,
            FromCompetitionStatus: null,
            ToCompetitionStatus: null,
            FromLeaderboardVisibility: null,
            ToLeaderboardVisibility: null,
            UserAccountAction: fact.Action,
            CompetitionEventKind: null,
            CompetitionEventLevel: null,
            CompetitionEventVisibility: null,
            RelatedUserId: fact.TargetUserId,
            TeamId: null,
            CompetitionChallengeId: null,
            RuntimeInstanceId: null,
            GameplayFactId: null,
            QuestionId: null,
            GameplayFactKind: null,
            GameplayFactState: null,
            GameplayFactResult: null,
            SubjectDisplayName: fact.TargetUserName,
            Reason: fact.Reason,
            Automatic: fact.Automatic,
            OccurredAt: notification.SentAt);
    }

}
